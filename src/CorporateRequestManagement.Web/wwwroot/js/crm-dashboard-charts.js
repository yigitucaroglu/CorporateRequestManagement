(() => {
  "use strict";

  const parseData = (section, selector) => {
    const source = section.querySelector(selector);
    if (!source) return null;
    try { return JSON.parse(source.textContent || "{}"); } catch { return null; }
  };

  const renderRequestChart = element => {
    const section = element.closest(".crm-request-chart");
    const model = section && parseData(section, "[data-request-chart-data]");
    if (!model || !Array.isArray(model.categories) || !Array.isArray(model.axisLabels) || !Array.isArray(model.tooltipLabels) || !Array.isArray(model.series) || typeof window.ApexCharts !== "function") return;
    if (model.categories.length === 0 || model.categories.length !== model.axisLabels.length || model.categories.length !== model.tooltipLabels.length || model.series.length === 0 || model.series.some(series => !Array.isArray(series.data) || series.data.length !== model.categories.length || series.data.some(value => !Number.isFinite(value)))) return;
    const rows = model.categories.map((category, index) => ({ category, axisLabel: String(model.axisLabels[index]), tooltipLabel: String(model.tooltipLabels[index]), values: model.series.map(series => series.data[index]) }))
      .sort((left, right) => String(left.category).localeCompare(String(right.category)));
    const series = model.series.map((item, seriesIndex) => ({ name: String(item.name), data: rows.map(row => row.values[seriesIndex]) }));
    const chart = new window.ApexCharts(element, {
      chart: { type: "area", height: 285, toolbar: { show: false }, fontFamily: "inherit", animations: { enabled: true, speed: 280 }, zoom: { enabled: false } },
      series,
      colors: ["#168be5", "#47aaa7"],
      stroke: { curve: "smooth", width: [3, 2.25], lineCap: "round" },
      fill: { type: "gradient", gradient: { shadeIntensity: 1, opacityFrom: 0.24, opacityTo: 0.025, stops: [0, 95, 100] } },
      dataLabels: { enabled: false },
      markers: { size: 0, hover: { size: 5 } },
      grid: { borderColor: "#e7edf6", strokeDashArray: 4, padding: { left: 8, right: 8, top: 2, bottom: 0 } },
      xaxis: { categories: rows.map(row => row.axisLabel), tickAmount: 6, labels: { rotate: 0, hideOverlappingLabels: true, style: { fontSize: "11px", colors: "#8993a7" } }, axisBorder: { color: "#d5ddea" }, axisTicks: { show: false }, tooltip: { enabled: false } },
      yaxis: { min: 0, forceNiceScale: true, decimalsInFloat: 0, labels: { formatter: value => Math.round(value).toString(), style: { fontSize: "11px", colors: ["#8993a7"] } } },
      tooltip: { shared: true, intersect: false, x: { formatter: (_, context) => rows[context.dataPointIndex]?.tooltipLabel ?? "" }, y: { formatter: value => Math.round(value).toString(), title: { formatter: name => `${name}: ` } } },
      legend: { show: false },
      noData: { text: "Talep hareketi bulunmuyor." }
    });
    chart.render().catch(error => console.error("Dashboard talep grafiği oluşturulamadı.", error));
  };

  const renderDistributionChart = element => {
    const section = element.closest(".crm-distribution");
    const model = section && parseData(section, "[data-distribution-chart-data]");
    if (!model || !Array.isArray(model.labels) || !Array.isArray(model.series) || typeof window.ApexCharts !== "function") return;
    const values = model.series.map(value => Number(value) || 0);
    const total = values.reduce((sum, value) => sum + value, 0);
    new window.ApexCharts(element, {
      chart: { type: "donut", height: 225, fontFamily: "inherit", animations: { enabled: true, speed: 280 } },
      labels: model.labels.map(String), series: values,
      colors: ["#168be5", "#47aaa7", "#f0b43d", "#dc6073", "#7869d9", "#8b98aa"],
      stroke: { width: 3, colors: ["#fff"] }, dataLabels: { enabled: false }, legend: { show: false },
      plotOptions: { pie: { donut: { size: "66%", labels: { show: true, name: { show: true, formatter: () => "Toplam" }, value: { show: true, formatter: () => total.toString() }, total: { show: true, label: "Toplam", formatter: () => total.toString() } } } } },
      tooltip: { y: { formatter: value => `${value} talep` } },
      states: { hover: { filter: { type: "lighten", value: 0.06 } }, active: { filter: { type: "none" } } }
    }).render();
  };

  document.querySelectorAll("[data-request-chart]").forEach(element => {
    try { renderRequestChart(element); } catch (error) { console.error("Dashboard talep grafiği başlatılamadı.", error); }
  });
  document.querySelectorAll("[data-distribution-chart]").forEach(element => {
    try { renderDistributionChart(element); } catch (error) { console.error("Dashboard dağılım grafiği başlatılamadı.", error); }
  });
})();
