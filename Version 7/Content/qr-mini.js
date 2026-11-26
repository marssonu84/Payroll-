/*
 * qr-mini.js - tiny offline QR Code generator (byte mode, error correction M, versions 1-20).
 * Runs entirely in the browser: the setup key is NEVER sent to any third-party QR service.
 * Usage: any element with a data-otpauth attribute is filled with an SVG QR code on page load.
 * Algorithm follows ISO/IEC 18004 (structure after Project Nayuki's MIT-licensed QR Code generator).
 */
(function (root) {
    'use strict';

    var ECC_PER_BLOCK = [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26];
    var NUM_BLOCKS    = [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16];
    var MAX_VERSION = 20;

    function utf8Bytes(str) {
        var s = unescape(encodeURIComponent(str)), out = [];
        for (var i = 0; i < s.length; i++) out.push(s.charCodeAt(i));
        return out;
    }

    function bit(x, i) { return ((x >>> i) & 1) !== 0; }

    function rawModules(ver) {
        var r = (16 * ver + 128) * ver + 64;
        if (ver >= 2) {
            var n = Math.floor(ver / 7) + 2;
            r -= (25 * n - 10) * n - 55;
            if (ver >= 7) r -= 36;
        }
        return r;
    }

    function dataCodewords(ver) {
        return Math.floor(rawModules(ver) / 8) - ECC_PER_BLOCK[ver] * NUM_BLOCKS[ver];
    }

    function alignPositions(ver) {
        if (ver === 1) return [];
        var n = Math.floor(ver / 7) + 2;
        var size = ver * 4 + 17;
        var step = Math.ceil((ver * 4 + 4) / (n * 2 - 2)) * 2;
        var res = [6];
        for (var pos = size - 7; res.length < n; pos -= step) res.splice(1, 0, pos);
        return res;
    }

    // ---- Reed-Solomon over GF(256), polynomial 0x11D ----
    function gfMul(x, y) {
        var z = 0;
        for (var i = 7; i >= 0; i--) {
            z = (z << 1) ^ ((z >>> 7) * 0x11D);
            z ^= ((y >>> i) & 1) * x;
        }
        return z & 0xFF;
    }

    function rsDivisor(degree) {
        var result = [];
        for (var i = 0; i < degree - 1; i++) result.push(0);
        result.push(1);
        var root = 1;
        for (var d = 0; d < degree; d++) {
            for (var j = 0; j < result.length; j++) {
                result[j] = gfMul(result[j], root);
                if (j + 1 < result.length) result[j] ^= result[j + 1];
            }
            root = gfMul(root, 0x02);
        }
        return result;
    }

    function rsRemainder(data, divisor) {
        var result = divisor.map(function () { return 0; });
        data.forEach(function (b) {
            var factor = b ^ result.shift();
            result.push(0);
            divisor.forEach(function (coef, i) { result[i] ^= gfMul(coef, factor); });
        });
        return result;
    }

    // ---- codeword assembly ----
    function buildCodewords(bytes, ver) {
        var bits = [];
        function put(val, len) { for (var i = len - 1; i >= 0; i--) bits.push((val >>> i) & 1); }

        put(4, 4);                                  // byte mode
        put(bytes.length, ver <= 9 ? 8 : 16);       // character count
        bytes.forEach(function (b) { put(b, 8); });

        var capBits = dataCodewords(ver) * 8;
        put(0, Math.min(4, capBits - bits.length)); // terminator
        while (bits.length % 8 !== 0) bits.push(0);
        for (var pad = 0xEC; bits.length < capBits; pad ^= 0xEC ^ 0x11) put(pad, 8);

        var data = [];
        for (var i = 0; i < bits.length; i += 8) {
            var v = 0;
            for (var j = 0; j < 8; j++) v = (v << 1) | bits[i + j];
            data.push(v);
        }

        var numBlocks = NUM_BLOCKS[ver], eccLen = ECC_PER_BLOCK[ver];
        var rawCw = Math.floor(rawModules(ver) / 8);
        var numShort = numBlocks - rawCw % numBlocks;
        var shortLen = Math.floor(rawCw / numBlocks);
        var divisor = rsDivisor(eccLen);
        var blocks = [];
        for (var b = 0, k = 0; b < numBlocks; b++) {
            var dat = data.slice(k, k + shortLen - eccLen + (b < numShort ? 0 : 1));
            k += dat.length;
            var ecc = rsRemainder(dat, divisor);
            if (b < numShort) dat.push(0);
            blocks.push(dat.concat(ecc));
        }
        var result = [];
        for (var x = 0; x < blocks[0].length; x++) {
            for (var y = 0; y < blocks.length; y++) {
                if (x !== shortLen - eccLen || y >= numShort) result.push(blocks[y][x]);
            }
        }
        return result;
    }

    // ---- matrix ----
    function makeMatrix(codewords, ver) {
        var size = ver * 4 + 17, m = [], f = [];
        for (var i = 0; i < size; i++) {
            m.push(new Array(size).fill(false));
            f.push(new Array(size).fill(false));
        }
        function setFn(x, y, dark) { m[y][x] = dark; f[y][x] = true; }

        function finder(cx, cy) {
            for (var dy = -4; dy <= 4; dy++) {
                for (var dx = -4; dx <= 4; dx++) {
                    var dist = Math.max(Math.abs(dx), Math.abs(dy));
                    var xx = cx + dx, yy = cy + dy;
                    if (xx >= 0 && xx < size && yy >= 0 && yy < size) setFn(xx, yy, dist !== 2 && dist !== 4);
                }
            }
        }
        function align(cx, cy) {
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                    setFn(cx + dx, cy + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
        }

        function formatBits(mask) {            // error correction level M = 0
            var data = (0 << 3) | mask, rem = data;
            for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
            return ((data << 10) | rem) ^ 0x5412;
        }
        function drawFormat(mask) {
            var bits = formatBits(mask), i;
            for (i = 0; i <= 5; i++) setFn(8, i, bit(bits, i));
            setFn(8, 7, bit(bits, 6));
            setFn(8, 8, bit(bits, 7));
            setFn(7, 8, bit(bits, 8));
            for (i = 9; i < 15; i++) setFn(14 - i, 8, bit(bits, i));
            for (i = 0; i < 8; i++) setFn(size - 1 - i, 8, bit(bits, i));
            for (i = 8; i < 15; i++) setFn(8, size - 15 + i, bit(bits, i));
            setFn(8, size - 8, true);
        }

        var i;
        for (i = 0; i < size; i++) { setFn(6, i, i % 2 === 0); setFn(i, 6, i % 2 === 0); }
        finder(3, 3); finder(size - 4, 3); finder(3, size - 4);
        var ap = alignPositions(ver), n = ap.length;
        for (i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                if (!((i === 0 && j === 0) || (i === 0 && j === n - 1) || (i === n - 1 && j === 0)))
                    align(ap[i], ap[j]);
        drawFormat(0);
        if (ver >= 7) {
            var rem = ver;
            for (i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >>> 11) * 0x1F25);
            var vbits = (ver << 12) | rem;
            for (i = 0; i < 18; i++) {
                var a = size - 11 + i % 3, b = Math.floor(i / 3), d = bit(vbits, i);
                setFn(a, b, d); setFn(b, a, d);
            }
        }

        // place data bits in the zigzag order
        var idx = 0;
        for (var right = size - 1; right >= 1; right -= 2) {
            if (right === 6) right = 5;
            for (var vert = 0; vert < size; vert++) {
                for (var c = 0; c < 2; c++) {
                    var x = right - c;
                    var upward = ((right + 1) & 2) === 0;
                    var y = upward ? size - 1 - vert : vert;
                    if (!f[y][x] && idx < codewords.length * 8) {
                        m[y][x] = bit(codewords[idx >>> 3], 7 - (idx & 7));
                        idx++;
                    }
                }
            }
        }

        function applyMask(mask) {
            for (var y = 0; y < size; y++) {
                for (var x = 0; x < size; x++) {
                    var inv;
                    switch (mask) {
                        case 0: inv = (x + y) % 2 === 0; break;
                        case 1: inv = y % 2 === 0; break;
                        case 2: inv = x % 3 === 0; break;
                        case 3: inv = (x + y) % 3 === 0; break;
                        case 4: inv = (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 === 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 === 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 === 0; break;
                    }
                    if (!f[y][x] && inv) m[y][x] = !m[y][x];
                }
            }
        }

        function penalty() {
            var score = 0, x, y, run, k;
            var pat = [true, false, true, true, true, false, true];
            function line(get) {
                run = 1;
                for (k = 1; k < size; k++) {
                    if (get(k) === get(k - 1)) { run++; if (run === 5) score += 3; else if (run > 5) score++; }
                    else run = 1;
                }
                for (k = 0; k + 7 <= size; k++) {
                    var ok = true;
                    for (var q = 0; q < 7; q++) if (get(k + q) !== pat[q]) { ok = false; break; }
                    if (!ok) continue;
                    var before = true, after = true;
                    for (q = 1; q <= 4; q++) {
                        if (k - q >= 0 && get(k - q)) before = false;
                        if (k + 6 + q < size && get(k + 6 + q)) after = false;
                    }
                    if (before || after) score += 40;
                }
            }
            for (y = 0; y < size; y++) line(function (t) { return m[y][t]; });
            for (x = 0; x < size; x++) line(function (t) { return m[t][x]; });
            for (y = 0; y < size - 1; y++)
                for (x = 0; x < size - 1; x++)
                    if (m[y][x] === m[y][x + 1] && m[y][x] === m[y + 1][x] && m[y][x] === m[y + 1][x + 1]) score += 3;
            var dark = 0;
            for (y = 0; y < size; y++) for (x = 0; x < size; x++) if (m[y][x]) dark++;
            var total = size * size;
            score += (Math.ceil(Math.abs(dark * 20 - total * 10) / total) - 1) * 10;
            return score;
        }

        var best = 0, bestScore = Infinity;
        for (var mask = 0; mask < 8; mask++) {
            applyMask(mask); drawFormat(mask);
            var p = penalty();
            if (p < bestScore) { bestScore = p; best = mask; }
            applyMask(mask);
        }
        applyMask(best); drawFormat(best);
        return m;
    }

    function encode(text) {
        var bytes = utf8Bytes(text), ver;
        for (ver = 1; ver <= MAX_VERSION; ver++) {
            var countBits = ver <= 9 ? 8 : 16;
            if (4 + countBits + bytes.length * 8 <= dataCodewords(ver) * 8) break;
        }
        if (ver > MAX_VERSION) throw new Error('Text too long for QR');
        return makeMatrix(buildCodewords(bytes, ver), ver);
    }

    function toSvg(text, px) {
        var m = encode(text), n = m.length, q = 4, d = [];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                if (m[y][x]) d.push('M' + (x + q) + ',' + (y + q) + 'h1v1h-1z');
        var dim = n + q * 2;
        return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + dim + ' ' + dim + '"' +
            ' width="' + (px || 220) + '" height="' + (px || 220) + '" shape-rendering="crispEdges"' +
            ' role="img" aria-label="QR code for authenticator app setup">' +
            '<rect width="100%" height="100%" fill="#ffffff"/>' +
            '<path d="' + d.join('') + '" fill="#000000"/></svg>';
    }

    function renderAll() {
        var boxes = document.querySelectorAll('[data-otpauth]');
        for (var i = 0; i < boxes.length; i++) {
            try {
                boxes[i].innerHTML = toSvg(boxes[i].getAttribute('data-otpauth'), 220);
            } catch (e) {
                boxes[i].textContent = 'QR code unavailable. Please use the setup key below.';
            }
        }
    }

    var api = { svg: toSvg, matrix: encode };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    else {
        root.QrMini = api;
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', renderAll);
        else renderAll();
    }
})(typeof window !== 'undefined' ? window : this);
