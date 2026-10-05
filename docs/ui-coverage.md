# UI coverage

**Status:** written 2026-10-05, when the UI was first completed.

This records where each item of the UI checklist in `spec/domain.md` §13 is
met, and how it was checked. `docs/counterpart-metrics.md` in tadmor asks
for a walk-through, item by item, before a counterpart's figures count;
this is the record to walk through.

**How it was checked.**

- **Test** means `tests/Tadmor.Tests/UiTests.cs` drives it over HTTP as a
  browser would. The real server runs in-process on `TEST_DATABASE_URL`,
  with cookies kept, forms posted with their anti-forgery tokens, and the
  pages read back. It runs with `make test`.
- **Browser** means a scripted walk-through in headless Chromium on
  2026-10-05. It used tadmor's Playwright install from outside this repo, so
  it is not a dependency here and nothing in the repo runs it. It drove the
  pages, including `wwwroot/app.js`, against a live server: 53 checks, all
  passing, among them the line editor's fills and exact previews, the
  confirmation dialogs (dismissed and accepted), and role-based hiding.
- **Smoke** means the screen was rendered in that browser against a
  database full of conformance-suite data, screenshotted, and inspected,
  with no console errors or Content Security Policy violations. No test
  asserts its content.

## General

| Item | Where | Checked |
| ---- | ----- | ------- |
| G1 | `/login`; every other UI path redirects there without a session, returning afterwards to the page asked for | Test, Browser (failed login, return, an ended session) |
| G2 | Name and Sign out in the header of every page | Test, Browser |
| G3 | The sidebar on every page | Smoke (every link opens) |
| G4 | Users, unpost, reopen (statements and years), and year-end hidden from non-administrators; the services refuse them with 403 | Test (Users 403, settings read-only), Browser (Users link, year-end) |
| G5 | A refusal re-shows the form or detail screen with the server's message above it, keeping what was typed | Test (duplicate warehouse, deleting a posted invoice), Browser (duplicate customer) |
| G6 | `data-confirm` on every delete (documents, payments, orders, movements, statements and their lines, exchange rates) and on unpost, close, and cancel | Browser (dismiss keeps the record, accept deletes) |
| G7 | Amounts exact and grouped, never rounded (`Ui/Format`); the currency beside document amounts | Test, Smoke |
| G8 | The not-found page for unknown addresses and records | Test |

## Home

| Item | Where | Checked |
| ---- | ----- | ------- |
| H1 to H5 | `/` | Test (content present), Smoke (figures) |

## Master data

| Item | Where | Checked |
| ---- | ----- | ------- |
| M1 to M5 | `/organizations`, `/customers`, `/suppliers`, `/products`, `/accounts`, `/tax-codes`, `/payment-terms`, `/warehouses`: one generic list and form (`Ui/Resources`, `Pages/Master`). Pickers offer active records, plus any inactive one the record already uses. The account form's parent picker leaves out the account itself | Test (create, refusal), Browser (organization, customer, tax code, product, warehouse), Smoke (every list and form) |
| M6 | A status column in each list; Active checkbox on the edit form only | Smoke |
| M7 | `/users`, with a separate Reset password page per user. Deactivating or demoting oneself is refused by the service and shown like any refusal | Test (create), Browser (create); the self-refusal comes from the users service, covered by the conformance suite |
| M8 | `/settings`, read-only for non-administrators | Test |

## Documents, payments, orders, inventory

| Item | Where | Checked |
| ---- | ----- | ------- |
| D1 | `/sales-invoices`, `/purchase-bills`, `/sales-credit-notes`, `/purchase-credit-notes` | Smoke |
| D2 | `/{collection}/new` and `/{collection}/{id}/edit`, with the line editor in `app.js` | Browser (product fills description, price, tax code and rate, account; previews 21.999 and 1.8149 exactly; adds and removes lines; saved total equals the preview) |
| D3 | `/{collection}/{id}` | Test, Browser (journal entry link) |
| D4 | Post, Edit (not for order-produced documents), Delete; Unpost for administrators; Apply for posted credit notes | Test (post, refused delete, unpost, delete), Browser (post, unpost, delete, no Edit on an order-produced invoice); credit-note Apply is the payments' Apply on another settler, Smoke |
| D5 | "Applied to" on a credit note | Smoke |
| D6 | The PDF action opens `/api/{collection}/{id}/pdf`, which the session cookie authorizes | Smoke; the PDF itself was checked when the API was built |
| D7 | Email form: blank means the address on file; shows the address used, or the error | Browser (501 shown, since SMTP is not configured) |
| P1 to P4 | `/customer-payments`, `/supplier-payments` | Browser (create, post, apply, the invoice listed under Applied to), Smoke (lists) |
| O1 to O4, O7 | `/sales-orders`, `/purchase-orders` | Browser (create, confirm), Smoke |
| O5 | `/{orders}/{id}/invoice` (or `bill`): remaining quantities filled in and lowerable, then to the new draft | Browser (partial invoice of 2 of 5) |
| O6 | `/{orders}/{id}/ship` (or `receive`): stocked lines only, then links to the movements created | Browser (receiving a purchase order) |
| S1 | `/stock-movements` | Smoke |
| S2 | The movement form: quantity as a magnitude, signed by type; an adjustment keeps its sign | Browser (an issue of 2 saved as −2) |
| S3 | `/stock-movements/{id}`: a receipt asks for the account to credit, proposing GRNI | Browser (GRNI proposed; refused visibly without product accounts; then posted) |

## Reports

| Item | Where | Checked |
| ---- | ----- | ------- |
| R1 to R4, R7, R8 | `/reports/{profit-and-loss, balance-sheet, cash-flow, trial-balance, ar-aging, ap-aging, inventory-valuation}`. A malformed date is explained and treated as blank | Test (malformed date), Smoke (sections, totals, and the identities on real data) |
| R5 | `/accounts/{id}/ledger` with a running balance, and currency and base columns when any line is foreign | Smoke |
| R6 | `/journal-entries/{id}` | Browser |

## Accounting

| Item | Where | Checked |
| ---- | ----- | ------- |
| A1 | `/periods` (each period closes or reopens in one step), `/periods/years/{new or id}`, `/periods/{new or id}`. A new period is proposed as the month after the latest | Browser (fiscal year created), Smoke |
| A2 | `/periods/years/{id}/close`: proposes Retained Earnings and lists what will happen; Reopen year for the latest closed year | Browser (close, then reopen) |
| A3 | `/exchange-rates` | Smoke |
| A4, A5 | `/bank-statements`, `/bank-statements/{id}` | Browser (create, CSV import, auto-match, reconcile, reopen), Smoke (manual match, unmatch, add and delete lines) |

## Not yet driven end to end

These render, and the services behind them pass the conformance suite, but
no check has clicked them through the UI: a credit note's Apply, shipping
a sales order (receiving was driven), and a statement's manual match,
unmatch, and line deletion. The browser walk-through is not in this repo;
moving its flows into `UiTests` would make them repeatable.
