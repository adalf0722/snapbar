const demo = document.querySelector("#selection-demo");
const toggle = demo.querySelector(".demo-toggle");

toggle.addEventListener("click", () => {
  const paused = demo.classList.toggle("demo-paused");
  toggle.textContent = paused ? "繼續示範" : "暫停示範";
});

demo.classList.add("demo-ready");
toggle.hidden = false;
