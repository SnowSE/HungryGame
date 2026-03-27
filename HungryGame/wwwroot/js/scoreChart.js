// HungryGame/wwwroot/js/scoreChart.js
window.scoreChart = {
    _chart: null,

    init(canvasId) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        const phasePlugin = {
            id: 'phaseBackground',
            beforeDraw(chart) {
                const { ctx: c, chartArea, scales } = chart;
                if (!chartArea) return;
                const bs = chart._battleStartedAt;
                const xMin = scales.x.getPixelForValue(scales.x.min);
                const xMax = scales.x.getPixelForValue(scales.x.max);
                const { top, bottom } = chartArea;

                c.save();

                if (bs != null) {
                    const bx = scales.x.getPixelForValue(bs);
                    // Eating phase
                    c.fillStyle = 'rgba(0,255,136,0.07)';
                    c.fillRect(xMin, top, bx - xMin, bottom - top);
                    // Battle phase
                    c.fillStyle = 'rgba(255,68,68,0.09)';
                    c.fillRect(bx, top, xMax - bx, bottom - top);
                    // Divider
                    c.strokeStyle = 'rgba(255,68,68,0.5)';
                    c.lineWidth = 1.5;
                    c.setLineDash([4, 3]);
                    c.beginPath(); c.moveTo(bx, top); c.lineTo(bx, bottom); c.stroke();
                    c.setLineDash([]);
                    // Label
                    c.fillStyle = 'rgba(255,68,68,0.7)';
                    c.font = '10px Segoe UI';
                    c.fillText('\u2694 Battle', bx + 4, top + 14);
                } else {
                    // No battle — entire background is eating (green)
                    c.fillStyle = 'rgba(0,255,136,0.07)';
                    c.fillRect(xMin, top, xMax - xMin, bottom - top);
                }

                c.restore();
            }
        };

        this._chart = new Chart(ctx, {
            type: 'line',
            data: { datasets: [] },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 800 },
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: '#16213e',
                        borderColor: '#ffffff20',
                        borderWidth: 1,
                        titleColor: '#a0a0b8',
                        bodyColor: '#e8e8e8',
                        callbacks: {
                            title: items => `${items[0].parsed.x.toFixed(1)}s`,
                            label: item => ` ${item.dataset.label}: ${item.parsed.y} pts`,
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'linear',
                        min: 0,
                        title: { display: true, text: 'Elapsed (seconds)', color: '#6c6c80', font: { size: 11 } },
                        ticks: { color: '#6c6c80', font: { size: 10 } },
                        grid: { color: '#ffffff08' },
                    },
                    y: {
                        min: 0,
                        title: { display: true, text: 'Score', color: '#6c6c80', font: { size: 11 } },
                        ticks: { color: '#6c6c80', font: { size: 10 } },
                        grid: { color: '#ffffff08' },
                    }
                }
            },
            plugins: [phasePlugin]
        });
    },

    render(datasets, battleStartedAt, maxTime) {
        if (!this._chart) return;

        this._chart._battleStartedAt = battleStartedAt ?? null;

        if (!datasets || datasets.length === 0) {
            this._chart.data.datasets = [];
            this._chart.options.scales.x.max = 1;
            this._chart.update();
            return;
        }

        this._chart.data.datasets = datasets.map(d => ({
            label: d.label,
            data: d.points,
            borderColor: d.color,
            backgroundColor: d.color + '22',
            borderWidth: 2.5,
            pointRadius: 0,
            pointHoverRadius: 5,
            tension: 0.3,
        }));

        this._chart.options.scales.x.max = maxTime > 0 ? maxTime : 1;
        this._chart.update();
    }
};
