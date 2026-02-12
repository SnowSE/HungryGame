let boardCanvas, boardCtx, serverUrl;
let offscreenCanvas, offscreenCtx;

const playerColors = [
    '#e6194b', '#3cb44b', '#4363d8', '#f58231', '#911eb4',
    '#42d4f4', '#f032e6', '#bfef45', '#fabed4', '#469990',
    '#dcbeff', '#9A6324', '#800000', '#aaffc3', '#808000', '#000075'
];

// Pre-parse player colors to RGBA for direct ImageData writes
const playerColorRGBA = playerColors.map(hex => [
    parseInt(hex.slice(1, 3), 16),
    parseInt(hex.slice(3, 5), 16),
    parseInt(hex.slice(5, 7), 16),
    255
]);

const PILL_R = 240, PILL_G = 192, PILL_B = 64;

function initBoard(canvasId, server) {
    boardCanvas = document.getElementById(canvasId);
    boardCtx = boardCanvas.getContext('2d');
    serverUrl = server;
    tick();
}

async function tick() {
    try {
        const resp = await fetch(serverUrl + '/board');
        if (resp.ok) {
            const cells = await resp.json();
            drawBoard(cells);
        }
    } catch (e) {
        console.error('Board fetch error:', e);
    }
    setTimeout(tick, 1000);
}

function drawBoard(cells) {
    if (cells.length === 0) return;

    const canvas = boardCanvas;
    const ctx = boardCtx;

    // Find dimensions
    let maxRow = 0, maxCol = 0;
    for (let i = 0; i < cells.length; i++) {
        const loc = cells[i].location;
        if (loc.row > maxRow) maxRow = loc.row;
        if (loc.column > maxCol) maxCol = loc.column;
    }
    const rows = maxRow + 1;
    const cols = maxCol + 1;

    // Update info text
    const info = document.getElementById('boardInfo');
    if (info) info.textContent = 'Board: ' + cols + ' x ' + rows;

    // Size canvas to container
    const container = canvas.parentElement;
    const w = container.clientWidth;
    const h = container.clientHeight;
    if (canvas.width !== w || canvas.height !== h) {
        canvas.width = w;
        canvas.height = h;
    }

    // Create/resize offscreen canvas (1 pixel per cell)
    if (!offscreenCanvas || offscreenCanvas.width !== cols || offscreenCanvas.height !== rows) {
        offscreenCanvas = document.createElement('canvas');
        offscreenCanvas.width = cols;
        offscreenCanvas.height = rows;
        offscreenCtx = offscreenCanvas.getContext('2d');
    }

    // Build ImageData: 1 pixel per cell
    const imgData = offscreenCtx.createImageData(cols, rows);
    const data = imgData.data;

    // Fill white
    for (let i = 0; i < data.length; i += 4) {
        data[i] = 255;
        data[i + 1] = 255;
        data[i + 2] = 255;
        data[i + 3] = 255;
    }

    // Single pass: write pixel colors and collect player cells for labels
    const playerCells = [];
    for (let i = 0; i < cells.length; i++) {
        const cell = cells[i];
        const loc = cell.location;
        const px = (loc.row * cols + loc.column) * 4;

        if (cell.occupiedBy) {
            const c = playerColorRGBA[(cell.occupiedBy.id - 1) % playerColorRGBA.length];
            data[px] = c[0];
            data[px + 1] = c[1];
            data[px + 2] = c[2];
            data[px + 3] = 255;
            playerCells.push(cell);
        } else if (cell.isPillAvailable) {
            data[px] = PILL_R;
            data[px + 1] = PILL_G;
            data[px + 2] = PILL_B;
            data[px + 3] = 255;
        }
    }

    // Blit the 1px-per-cell image, then scale it up with crisp pixels
    offscreenCtx.putImageData(imgData, 0, 0);
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(offscreenCanvas, 0, 0, w, h);

    const cellW = w / cols;
    const cellH = h / rows;

    // Grid lines (only if cells are large enough to see)
    if (cellW >= 3 && cellH >= 3) {
        ctx.strokeStyle = '#ddd';
        ctx.lineWidth = 0.5;
        ctx.beginPath();
        for (let r = 0; r <= rows; r++) {
            const y = Math.round(r * cellH) + 0.5;
            ctx.moveTo(0, y);
            ctx.lineTo(w, y);
        }
        for (let c = 0; c <= cols; c++) {
            const x = Math.round(c * cellW) + 0.5;
            ctx.moveTo(x, 0);
            ctx.lineTo(x, h);
        }
        ctx.stroke();
    }

    // Player IDs (only when cells are large enough to read)
    if (cellW > 14 && cellH > 14) {
        ctx.fillStyle = '#fff';
        ctx.font = 'bold ' + Math.floor(Math.min(cellW, cellH) * 0.55) + 'px sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        for (let i = 0; i < playerCells.length; i++) {
            const cell = playerCells[i];
            ctx.fillText(
                cell.occupiedBy.id.toString(),
                cell.location.column * cellW + cellW / 2,
                cell.location.row * cellH + cellH / 2
            );
        }
    }
}
