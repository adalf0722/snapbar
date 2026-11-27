const demo = document.querySelector(".workflow-demo");

if (demo) {
  const controls = demo.querySelector(".demo-controls");
  const toggle = demo.querySelector(".demo-toggle");
  const replay = demo.querySelector(".demo-replay");
  const motion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const progress = demo.querySelectorAll(".demo-progress li");
  const durations = [200, 400, 600, 450, 1350];
  const progressSteps = [0, 0, 1, 2, 3];
  const poster = 2;
  let step = motion.matches ? poster : 0;
  let paused = motion.matches;
  let restartOnPlay = motion.matches;
  let visible = true;
  let timer;

  function render() {
    demo.dataset.step = String(step);
    progress.forEach((item, index) => {
      if (index === progressSteps[step]) item.setAttribute("aria-current", "step");
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

  function replayFromStart() {
    step = 0;
    paused = false;
    restartOnPlay = false;
    render();
    schedule();
  }

  toggle.addEventListener("click", () => {
    if (restartOnPlay) {
      replayFromStart();
      return;
    }
    paused = !paused;
    render();
    schedule();
  });
  replay.addEventListener("click", replayFromStart);
  motion.addEventListener("change", () => {
    paused = motion.matches;
    step = paused ? poster : 0;
    restartOnPlay = paused;
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
