# SnapBar official website

Open `index.html` locally to preview. The site uses HTML, CSS, a small release lookup script and actual product screenshots.

The homepage uses screenshots of the action bar and action settings, plus a rewrite example. The optional selection demo animates over the action bar screenshot. Update the overlay positions if the screenshot crop changes.

## Publish on GitHub Pages

1. Merge the site and workflow into the default branch. When ready for public access, make the GitHub repository public.
2. In repository **Settings → Pages → Build and deployment**, set **Source** to **GitHub Actions**.
3. Open **Actions → Deploy SnapBar website → Run workflow** and select the default branch. The workflow uploads only `site/`. Run it again after a site update.
4. Open the deployment URL. Check the Windows download link while signed out and verify that a published release includes the `SnapBar-*-win-x64.zip` asset.

The download buttons link to a verified Windows ZIP and update to the newest matching release when the public GitHub Releases API is available. While the repository is private, visitors need GitHub access. Update both ZIP links, the release notes link, and the displayed version in `index.html` when refreshing the pinned preview.
