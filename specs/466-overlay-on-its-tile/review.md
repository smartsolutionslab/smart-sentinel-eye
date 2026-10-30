# Phase 6 review — spec 466 (`/code-review high`, 2026-10-30)

Nine findings. Each is fixed, shown to be unfounded, or accepted here in writing.

| # | Finding | Disposition |
|---|---|---|
| 1 | A tile in both the Draft and the Published revision of one layout counted twice, so a single wall tile read "shared". | **Fixed.** A layout counts by its fullest open revision (owner decision). The test's expected number changed from 3 to 2 for that reason. |
| 2 | "Edit this overlay" could be pressed again while the draft was being made; the second call was refused as stale and showed the conflict message. | **Fixed.** Red first; the button is unavailable while the draft is made. |
| 3 | The open tile in "In the layout" showed the saved overlay while the large tile showed the live edit. | **Fixed.** Red first; the open context tile takes the live elements. |
| 4 | The `initialCamera` prop sat between `renderFrameGrabber` and its documentation. | **Fixed.** Moved. |
| 5 | Context tile key `row:col` could repeat for unsaved cells. | **Fixed.** Red first; keyed by index. |
| 6 | The form's first `onElementsChange` could report the create-mode default. | **Not reproduced.** The form seeds `useForm` with the draft's elements, so the first report is the draft's own. A guard test records it (`OverlayDraftForm.test.tsx`, green on arrival). |
| 7 | Opening "Edit" branches a real draft on the server; Cancel does not remove it. | **Accepted.** The form edits drafts only, so a published overlay has to be branched first. Stated in the PR. |
| 8 | The token-ref boilerplate is a third copy. | **Accepted.** An existing pattern; a shared hook is its own refactor. |
| 9 | The dialog content unmounts before its exit animation. | **Accepted.** Cosmetic; the unmount is what releases the captures. |
| 10 | A resolve request per keystroke while typing a placeholder. | **Fixed.** Red first; the texts are debounced like the editor's. While a placeholder text is being typed its label is blank until the pause, then resolved. |
