const preview = document.querySelector(".selection-preview");
const play = document.querySelector(".play-bubble");
const label = play.querySelector(".play-label");
const icon = play.querySelector(".play-icon");
const status = document.querySelector(".play-status");

play.addEventListener("click", () => {
  if (preview.classList.contains("is-playing")) return;

  play.setAttribute("aria-disabled", "true");
  label.textContent = "正在播放選字示範";
  icon.textContent = "▶";
  status.textContent = "拖曳選取文字，放開滑鼠後顯示動作列。";
  preview.classList.add("is-playing");
});

preview.querySelector(".selection-cover").addEventListener("animationend", () => {
  preview.classList.remove("is-playing");
  play.removeAttribute("aria-disabled");
  label.textContent = "重播示範";
  icon.textContent = "↻";
  status.textContent = "示範播放完畢。";
});

play.hidden = false;
