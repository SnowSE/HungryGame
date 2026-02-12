window.boardRenderer = {
    canvas: null,
    ctx: null,
    offscreen: null,
    offCtx: null,

    // Match the neon theme player colors from site.css
    playerColors: [
        [0, 255, 136],   // neon-green
        [255, 0, 110],    // neon-pink
        [0, 180, 216],    // neon-cyan
        [255, 214, 10],   // neon-yellow
        [255, 140, 0],    // neon-orange
        [179, 136, 255],  // neon-purple
        [0, 255, 221],    // teal
        [255, 102, 170],  // pink
        [136, 255, 0],    // lime
        [255, 136, 102]   // salmon
    ],

    // bg-surface color for empty cells
    EMPTY_R: 26, EMPTY_G: 26, EMPTY_B: 46,
    // pill color (subtle green tint)
    PILL_R: 30, PILL_G: 50, PILL_B: 45,
    // pill dot (neon green)
    PILL_DOT_R: 0, PILL_DOT_G: 200, PILL_DOT_B: 100,

    init: function (canvasId) {
        this.canvas = document.getElementById(canvasId);
        if (this.canvas) {
            this.ctx = this.canvas.getContext('2d');
        }
    },

    render: function (rows, cols, gridData, players) {
        var canvas = this.canvas;
        var ctx = this.ctx;
        if (!canvas || !ctx || rows === 0 || cols === 0) return;

        // Size canvas to container
        var container = canvas.parentElement;
        var w = container.clientWidth;
        var h = container.clientHeight;
        if (canvas.width !== w || canvas.height !== h) {
            canvas.width = w;
            canvas.height = h;
        }

        // Create/resize offscreen canvas (1 pixel per cell)
        if (!this.offscreen || this.offscreen.width !== cols || this.offscreen.height !== rows) {
            this.offscreen = document.createElement('canvas');
            this.offscreen.width = cols;
            this.offscreen.height = rows;
            this.offCtx = this.offscreen.getContext('2d');
        }

        var offCtx = this.offCtx;
        var imgData = offCtx.createImageData(cols, rows);
        var px = imgData.data;
        var colors = this.playerColors;
        var colorCount = colors.length;

        // Single pass: write pixels from compact byte array
        for (var i = 0; i < gridData.length; i++) {
            var p = i * 4;
            var v = gridData[i];
            if (v === 0) {
                // empty
                px[p] = this.EMPTY_R;
                px[p + 1] = this.EMPTY_G;
                px[p + 2] = this.EMPTY_B;
            } else if (v === 1) {
                // pill
                px[p] = this.PILL_R;
                px[p + 1] = this.PILL_G;
                px[p + 2] = this.PILL_B;
            } else {
                // player: color index is (v - 2) % colorCount
                var c = colors[(v - 2) % colorCount];
                px[p] = c[0];
                px[p + 1] = c[1];
                px[p + 2] = c[2];
            }
            px[p + 3] = 255;
        }

        // Blit 1px-per-cell image, then scale up with crisp pixels
        offCtx.putImageData(imgData, 0, 0);
        ctx.imageSmoothingEnabled = false;
        ctx.drawImage(this.offscreen, 0, 0, w, h);

        var cellW = w / cols;
        var cellH = h / rows;

        // Draw pill dots when cells are large enough
        if (cellW >= 4 && cellH >= 4) {
            ctx.fillStyle = 'rgba(0, 255, 136, 0.6)';
            for (var i = 0; i < gridData.length; i++) {
                if (gridData[i] === 1) {
                    var row = Math.floor(i / cols);
                    var col = i % cols;
                    var cx = col * cellW + cellW / 2;
                    var cy = row * cellH + cellH / 2;
                    var r = Math.min(cellW, cellH) * 0.2;
                    ctx.beginPath();
                    ctx.arc(cx, cy, r, 0, 6.283);
                    ctx.fill();
                }
            }
        }

        // Grid lines (only if cells are large enough)
        if (cellW >= 3 && cellH >= 3) {
            ctx.strokeStyle = 'rgba(255, 255, 255, 0.04)';
            ctx.lineWidth = 0.5;
            ctx.beginPath();
            for (var r = 0; r <= rows; r++) {
                var y = Math.round(r * cellH) + 0.5;
                ctx.moveTo(0, y);
                ctx.lineTo(w, y);
            }
            for (var c = 0; c <= cols; c++) {
                var x = Math.round(c * cellW) + 0.5;
                ctx.moveTo(x, 0);
                ctx.lineTo(x, h);
            }
            ctx.stroke();
        }

        // Player IDs (only when cells are large enough to read)
        if (cellW > 14 && cellH > 14 && players) {
            ctx.fillStyle = 'rgba(0, 0, 0, 0.8)';
            ctx.font = 'bold ' + Math.floor(Math.min(cellW, cellH) * 0.45) + 'px Orbitron, sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            for (var i = 0; i < players.length; i++) {
                var pl = players[i];
                ctx.fillText(
                    pl[2].toString(),
                    pl[1] * cellW + cellW / 2,
                    pl[0] * cellH + cellH / 2
                );
            }
        }
    }
};
