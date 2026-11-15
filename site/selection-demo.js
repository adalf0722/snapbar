const demo = document.querySelector(".workflow-demo");

if (demo) {
  const controls = demo.querySelector(".demo-controls");
  const toggle = demo.querySelector(".demo-toggle");
  const replay = demo.querySelector(".demo-replay");
  const motion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const durations = [1500, 1500, 2000, 3000];
  let step = motion.matches ? 3 : 0;
  let paused = motion.matches;
  let visible = true;
  let timer;

  function render() {
    demo.dataset.step = String(step);
    demo.querySelectorAll(".demo-progress li").forEach((item, index) => {
      if (index === step) item.setAttribute("aria-current", "step");
      else item.removeAttribute("aria-current");
    });
    toggle.textContent = paused ? toggle.dataset.play : toggle.dataset.pause;
  }

  function schedule() {
    clearTimeout(timer);
    if (paused || !visible || document.hidden) return;
    timer = setTimeout(() => {
      step = (step + 1) % durations.length;
      render();
      schedule();
    }, durations[step]);
  }

  toggle.addEventListener("click", () => {
    paused = !paused;
    render();
    schedule();
  });
  replay.addEventListener("click", () => {
    step = 0;
    paused = false;
    render();
    schedule();
  });
  motion.addEventListener("change", () => {
    paused = motion.matches;
    step = paused ? 3 : 0;
    render();
    schedule();
  });
  document.addEventListener("visibilitychange", schedule);
  if ("IntersectionObserver" in window) {
    new IntersectionObserver(([entry]) => {
      visible = entry.isIntersecting;
      schedule();
    }).observe(demo);
  }

  controls.hidden = false;
  render();
  schedule();
}
