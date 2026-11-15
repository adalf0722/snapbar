# SnapBar official website

Open `index.html` locally to preview. The site uses HTML, CSS and an illustrative selection demo. Source-page download links lead to the Releases list; deployment generates direct ZIP links and version labels from published Release metadata.

`en/index.html` provides the English page; each page links to the other language. Both use the same three-part narrative: select and search, optionally rewrite a draft with AI, then download and follow first-run practice. Both share `selection-demo.js` and `style.css`. Keep local asset URLs relative to the page's directory.

The hero automatically plays an eight-second illustrative workflow: select a term, press Ctrl+Alt+V, choose Search, then see a browser search illustration. This is HTML/CSS, not a product recording, live search or AI request. Pause and replay controls use each page's language; playback pauses outside the viewport or when the tab is hidden. Reduced-motion visitors and visitors without JavaScript see the complete static result. AI rewrite appears only in the second section, with a before/after example and its separate service/key requirements. Toolbar customization and script requirements are in an expandable FAQ, not a separate feature section. The download steps match the built-in first-run search practice.

## Publish on GitHub Pages

1. Merge the site and workflow into the default branch. When ready for public access, make the GitHub repository public.
2. In repository **Settings → Pages → Build and deployment**, set **Source** to **GitHub Actions**.
3. After an automatic main prerelease is published, or a manual Release has its verified assets uploaded, the Release workflow calls **Deploy SnapBar website**. Other branch/tag pushes and validation-only runs do not deploy. For a website-only refresh or deployment retry, use **Actions → Deploy SnapBar website → Run workflow**; this does not create another Release.
4. Open the deployment URL. Check the Windows download link while signed out and verify that a published release includes the `SnapBar-*-win-x64.zip` asset.

The reusable Pages workflow checks out current `main`, then runs `scripts/build-site.ps1` with the workflow's read token. It selects the most recently **published** eligible release, including prereleases, with both a nonempty uploaded `SnapBar-<tag>-win-x64.zip` and its `.sha256` file at the expected repository URLs. It does not use GitHub's stable-only `/releases/latest`, API ordering, or tag spelling to choose recency. Drafts and incomplete releases are skipped. Retrying an older Release workflow therefore selects the newest complete public download rather than pinning the website to the old run.

Both language pages are validated before a fresh deployment directory is created. The generated HTML contains direct ZIP links, matching release notes and a version label, including when visitors disable JavaScript. Browsers make no Release API request. API errors, missing packages or invalid page markers fail before uploading a Pages artifact; the existing website remains deployed. The workflow only generates a temporary artifact: it never commits or pushes a version update. No additional PAT or secret is required. Existing `github-pages` environment rules still apply; allow the main branch and any release tags you intend to deploy from.

To preview the generated download links locally in a **new** output directory:

```powershell
pwsh -NoProfile -File scripts/build-site.ps1 -Repository adalf0722/snapbar -OutputDirectory artifacts/site-preview
pwsh -NoProfile -File scripts/test-site.ps1
```

For a private repository, the build needs a `GH_TOKEN` with read access, and visitors still need access to download its Release assets. Never embed a token in the website. Check release notes for language support and features in the downloaded desktop build.
