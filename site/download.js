async function linkWindowsPackage() {
  const releasesUrl = "https://github.com/adalf0722/snapbar/releases";
  const response = await fetch("https://api.github.com/repos/adalf0722/snapbar/releases");
  if (!response.ok) return;

  const releases = await response.json();
  for (const release of releases) {
    if (release.draft || !/^v\d+\.\d+\.\d+(?:-[\w.-]+)?$/.test(release.tag_name)) continue;

    const filename = `SnapBar-${release.tag_name}-win-x64.zip`;
    const downloadUrl = `${releasesUrl}/download/${release.tag_name}/${filename}`;
    if (!release.assets.some(asset => asset.state === "uploaded" && asset.browser_download_url === downloadUrl)) continue;

    for (const link of document.querySelectorAll("[data-windows-download]")) {
      link.href = downloadUrl;
    }
    document.querySelector("#release-notes").href = `${releasesUrl}/tag/${release.tag_name}`;
    document.querySelector("#download-version").textContent = release.tag_name;
    return;
  }
}

// Keep the verified ZIP link when the repository or API is unavailable publicly.
linkWindowsPackage().catch(() => {});
