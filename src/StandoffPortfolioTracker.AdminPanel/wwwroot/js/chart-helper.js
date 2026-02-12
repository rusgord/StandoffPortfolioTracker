let chartInstance = null;

window.drawPriceChart = function(dates, prices, itemName, periodDays = 90) {
    const ctx = document.getElementById('priceChart');
    if (!ctx) return;

    if (chartInstance) {
        chartInstance.destroy();
    }

    // Gradient for fill (blue to transparent)
    const gradient = ctx.getContext('2d').createLinearGradient(0, 0, 0, 300);
    gradient.addColorStop(0, 'rgba(13, 110, 253, 0.25)'); // Bootstrap Primary Blue
    gradient.addColorStop(1, 'rgba(13, 110, 253, 0.0)');

    // Format dates
    const formattedDates = dates.map(d => {
        const date = new Date(d);
        // Adjust label frequency based on period
        if (periodDays <= 7) {
            return date.toLocaleDateString('ru-RU', { month: 'short', day: 'numeric' });
        } else if (periodDays <= 30) {
            return date.toLocaleDateString('ru-RU', { month: 'short', day: 'numeric' });
        } else {
            return date.toLocaleDateString('ru-RU', { month: 'short', day: 'numeric' });
        }
    });

    // Calculate max ticks to display based on period
    const maxTicksLimit = periodDays <= 7 ? 7 : periodDays <= 30 ? 10 : periodDays <= 90 ? 12 : 15;

    chartInstance = new Chart(ctx, {
        type: 'line',
        data: {
            labels: formattedDates,
            datasets: [{
                label: 'Цена',
                data: prices,
                borderColor: '#0d6efd', // Bright blue
                backgroundColor: gradient,
                borderWidth: 3,
                fill: true,
                tension: 0.4, // Smooth lines
                pointRadius: 0, // Hide points until hover
                pointHoverRadius: 6,
                pointHoverBackgroundColor: '#fff',
                pointHoverBorderColor: '#0d6efd',
                pointHoverBorderWidth: 3
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'index',
                intersect: false,
            },
            plugins: {
                legend: { display: false }, // Hide legend
                tooltip: {
                    backgroundColor: 'rgba(33, 37, 41, 0.95)', // Dark tooltip
                    titleColor: '#fff',
                    bodyColor: '#fff',
                    bodyFont: { size: 14, weight: 'bold' },
                    padding: 12,
                    cornerRadius: 8,
                    displayColors: false,
                    callbacks: {
                        title: function(context) {
                            return context[0].label;
                        },
                        label: function(context) {
                            return context.raw.toFixed(2) + ' G';
                        }
                    }
                }
            },
            scales: {
                x: {
                    grid: { display: false }, // Hide vertical grid
                    ticks: {
                        color: '#adb5bd',
                        maxTicksLimit: maxTicksLimit,
                        maxRotation: 0,
                        minRotation: 0
                    }
                },
                y: {
                    border: { display: false },
                    grid: {
                        color: '#f1f5f9', // Very faint horizontal grid
                        borderDash: [5, 5]
                    },
                    ticks: {
                        color: '#6c757d',
                        font: { size: 11 },
                        callback: function(value) { return value.toFixed(0); }
                    }
                }
            }
        }
    });
};