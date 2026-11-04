# SnapBar official website

Open `index.html` locally to preview. The site uses HTML, CSS, a small playback script and actual product screenshots.

The walkthrough video is still pending. The homepage uses an animated illustration of the selection flow, a crop of the action settings screenshot and a rewrite example. Refresh the illustration and crop when the desktop UI changes.

## Publish on GitHub Pages

1. Merge the site and workflow into the default branch. When ready for public access, make the GitHub repository public.
2. In repository **Settings → Pages → Build and deployment**, set **Source** to **GitHub Actions**.
3. Open **Actions → Deploy SnapBar website → Run workflow** and select the default branch. The workflow uploads only `site/`. Run it again after a site update.
4. Open the deployment URL. Check the Windows download link while signed out and verify that a published release includes the `SnapBar-*-win-x64.zip` asset.

The website links to the Releases list so visitors can see prereleases. It does not need a website edit for each version.
