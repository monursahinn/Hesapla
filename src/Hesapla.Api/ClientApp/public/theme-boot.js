// Son kullanılan temanın zeminiyle açılsın (beyaz/siyah parlaması olmasın).
(function () {
  var bg = { Dark: "#0d121b", DarkBlue: "#0c132d", Turquoise: "#e3f8fb", Sand: "#f3eee3", Gray: "#e7eaee" };
  var t = "Light";
  try { t = localStorage.getItem("hesapla.theme") || "Light"; } catch (e) {}
  var dark = t === "Dark" || t === "DarkBlue";
  var s = document.documentElement.style;
  s.setProperty("--splash-bg", bg[t] || "#f4f6f9");
  s.setProperty("--splash-fg", dark ? "#e5e7eb" : "#1f2937");
})();
