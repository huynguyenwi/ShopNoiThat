/* Admin dashboard charts (Chart.js, loaded locally). */
(function (window, document) {
    'use strict';

    const source = document.getElementById('dashboardData');
    if (!source || !window.Chart) return;

    const data = JSON.parse(source.textContent);
    const wood = '#b07d4f';
    const brown = '#5b3a24';
    const sage = '#7f9270';
    const palette = ['#d4a24c', '#6c9bd1', '#5b3a24', '#8e7cc3', '#3f7d4e', '#9a9a9a', '#2b2521'];
    const money = function (value) { return new Intl.NumberFormat('vi-VN').format(value) + '₫'; };
    const shortMoney = function (value) {
        if (value >= 1e9) return (value / 1e9).toFixed(1) + ' tỷ';
        if (value >= 1e6) return Math.round(value / 1e6) + ' tr';
        return new Intl.NumberFormat('vi-VN').format(value);
    };

    window.Chart.defaults.font.family = '"Be Vietnam Pro", system-ui, sans-serif';
    window.Chart.defaults.color = '#7a6d62';

    new window.Chart(document.getElementById('revenueDayChart'), {
        data: {
            labels: data.revenueByDay.labels,
            datasets: [
                { type: 'bar', label: 'Doanh thu', data: data.revenueByDay.values, backgroundColor: wood, borderRadius: 4, yAxisID: 'y' },
                { type: 'line', label: 'Số đơn', data: data.ordersByDay, borderColor: brown, backgroundColor: brown, tension: .3, yAxisID: 'y1', pointRadius: 2 }
            ]
        },
        options: {
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            scales: {
                y: { beginAtZero: true, ticks: { callback: shortMoney } },
                y1: { beginAtZero: true, position: 'right', grid: { drawOnChartArea: false }, ticks: { precision: 0 } }
            },
            plugins: { tooltip: { callbacks: { label: function (ctx) { return ctx.dataset.label + ': ' + (ctx.dataset.yAxisID === 'y' ? money(ctx.parsed.y) : ctx.parsed.y); } } } }
        }
    });

    new window.Chart(document.getElementById('revenueMonthChart'), {
        type: 'line',
        data: {
            labels: data.revenueByMonth.labels,
            datasets: [{ label: 'Doanh thu', data: data.revenueByMonth.values, borderColor: wood, backgroundColor: 'rgba(176,125,79,.15)', fill: true, tension: .3 }]
        },
        options: {
            maintainAspectRatio: false,
            scales: { y: { beginAtZero: true, ticks: { callback: shortMoney } } },
            plugins: { legend: { display: false }, tooltip: { callbacks: { label: function (ctx) { return money(ctx.parsed.y); } } } }
        }
    });

    new window.Chart(document.getElementById('statusChart'), {
        type: 'doughnut',
        data: { labels: data.ordersByStatus.labels, datasets: [{ data: data.ordersByStatus.values, backgroundColor: palette, borderWidth: 2 }] },
        options: { maintainAspectRatio: false, plugins: { legend: { position: 'bottom', labels: { boxWidth: 12 } } } }
    });

    new window.Chart(document.getElementById('topProductsChart'), {
        type: 'bar',
        data: { labels: data.topProducts.labels, datasets: [{ label: 'Số lượng bán', data: data.topProducts.values, backgroundColor: sage, borderRadius: 4 }] },
        options: { indexAxis: 'y', maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { x: { beginAtZero: true, ticks: { precision: 0 } } } }
    });
})(window, document);
