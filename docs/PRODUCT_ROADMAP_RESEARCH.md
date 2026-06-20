# SQL Pilot — Feature & Monetization Research

*Research synthesis, June 2026. Grounded in the current codebase, competitor analysis, and real SSMS-user pain points.*

## TL;DR

SQL Pilot today is a fast, keyboard-driven **object search + navigation + scripting** extension for SSMS 18/20/22. Its single biggest strategic asset is that the SSMS 21/22 move to the 64-bit VS 2022 shell **broke or orphaned most of the free competition** (SQL Hunting Dog is stale since ~2016; ssms-schema-folders is broken on SSMS 22; ApexSQL's free tier was killed by Quest). SQL Pilot's dual-build, cross-version-reflection architecture is purpose-built for exactly this transition.

Recommendation: **defend and widen the search/navigation lead, add the most-requested "never lose work" features as free wedges, and monetize via a low-friction open-core "Pro" tier** (perpetual license, ~$39–49, honor-system trial) — the model proven by SSMS Tools Pack, the closest comparable.

---

## Part 1 — Features to add (ranked by user demand × fit × effort)

Each feature is tagged **[FREE]** (keep the core attractive and viral) or **[PRO]** (paywall candidate). Reasoning for the split is in Part 2.

### Tier A — High demand, strong architectural fit, ship next

1. **Query history / "never lose a query"** **[FREE core, PRO advanced]**
   - *Need:* The #2 most-requested SSMS capability. SSMS has no permanent query log; AutoRecover only helps on crash, not normal tab-close. Every paid tool markets this.
   - *SQL Pilot fit:* You already have a local line-based store (`LineStore`), an LRU recents store, and document/execution hooks in `ScriptingBridge`. Capturing executed SQL into a searchable history reuses all of it — and your Ctrl+D fuzzy search becomes the natural way to *find* past queries.
   - *Split:* Free = last N queries, session history. Pro = unlimited/persistent history, full-text search across history, per-server grouping, restore-on-startup.

2. **Tab recovery / restore last session (incl. unsaved scripts)** **[PRO]**
   - *Need:* Closely tied to #1; SSMSBoost's headline feature. Users specifically search for "reopen closed tab in SSMS."
   - *Fit:* Builds on the same document-event plumbing already used for auto-execute.

3. **Connection / tab coloring by environment (prod safety)** **[FREE]**
   - *Need:* Widely discussed as disaster-prevention (running DROP against prod). Native SSMS coloring is clunky; paid tools all enhance it.
   - *Fit:* You already walk Object Explorer connections (`ConnectionWatcher`, `ObjectExplorerBridge`) and resolve server URNs. Mapping server → color and tinting the query window is a contained add. Strong **free** feature: high safety value, viral word-of-mouth, low cannibalization of Pro.

4. **Schema object actions** **[FREE]**
   - *Need:* Schemas are already indexed but have **no context-menu actions** (noted gap in `SearchControl.xaml.cs`). Low-hanging fruit: "script schema," "filter search to this schema," "list objects in schema."
   - *Fit:* Trivial — closes an existing inconsistency.

5. **Settings / preferences UI** **[FREE]**
   - *Need:* Settings exist (`SqlPilotSettings`: `SelectTopNCount`, `SearchDebounceMs`, `CheckForUpdates`, etc.) but are file-only with no UI.
   - *Fit:* An SSMS Options page or in-tool-window panel. Also the natural home for a future license-key field, coloring config, and rebindable hotkey.

6. **Rebindable hotkey** **[FREE]**
   - *Need:* Ctrl+D is hardcoded and collides with some users' muscle memory (it's "duplicate line" elsewhere). No rebinding UI exists today.
   - *Fit:* You register via Win32 `RegisterHotKey`; exposing the key combo through the settings UI is straightforward.

### Tier B — High value, more effort

7. **"Search inside object definitions" (find-references / text search across DBs)** **[FREE teaser, PRO full]**
   - *Need:* This is exactly what Redgate SQL Search (free) does and why it's popular — find every proc/view that references a column or string. A frequent DBA task.
   - *Fit:* You already pull object definitions for scripting. Indexing definition bodies extends your existing search engine. Free = search current DB; Pro = search across all servers/DBs at once with grouped results.

8. **Results grid enhancements / smarter export** **[PRO]**
   - *Need:* Long-standing grid pain (copy-with-headers quirks, broken Excel paste). SSMS only added good export formats in 22.4.1.
   - *Fit:* More involved (grid interop), but a clear Pro differentiator: copy-with-headers done right, export to Excel/CSV/JSON/Markdown, "generate INSERTs from grid."

9. **Snippet / template manager** **[PRO]**
   - *Need:* Native snippets are XML-file-edited and awkward; a recurring ask and a paid-tool staple.
   - *Fit:* Reuses your WPF tool-window UI patterns and local store. Pairs well with the search UX (Ctrl+D to insert a snippet).

10. **Generate `SELECT`/`INSERT`/`UPDATE` column lists, "Expand SELECT \*"** **[PRO]**
    - *Need:* Classic SSMS Tools Pack / SQL Prompt feature; small but beloved time-savers.
    - *Fit:* You already enumerate columns via SMO for scripting.

### Tier C — Deliberately NOT recommended (now)

- **Full IntelliSense/autocomplete replacement.** It's the #1 user complaint *and* the #1 reason people buy SQL Prompt — but it's enormous, high-risk engineering (parser, caching, cross-version SSMS editor interop) and a direct fight with Redgate/Devart. Not where a solo-maintained tool should plant its flag. Revisit only if SQL Pilot gains real traction and revenue.
- **Execution-plan analysis.** Valuable but a separate product category (Plan Explorer, Erik Darling's Performance Studio already own it). Out of scope for a search/navigation tool.

---

## Part 2 — Monetization path (in plain terms)

### The core idea: "open-core"

Keep the SQL Pilot you have today **free and Apache-2.0 forever** — search, navigation, scripting, favorites, recents, environment coloring. Add a small set of **"Pro" power-user features behind a paid license key**. The free tier stays genuinely useful (and keeps growing the user base and word-of-mouth); the Pro tier funds the work.

This is not theoretical — it's exactly how the closest comparable survived.

### The proof point: SSMS Tools Pack

A **solo-developer SSMS add-in** that went from free to paid in 2012 at **~$30**, grandfathered existing users, suffered **no real backlash**, and is **still actively maintained 14+ years later** (shipped SSMS 21/22 support and new features in early 2026). It's perpetual (one-time), not subscription. That's the template.

### What converts (from the broader indie-tool data)

| Lesson | Evidence |
|---|---|
| **Perpetual one-time beats subscription** for indie dev tools | Beyond Compare, Sublime (personal), Fork, Quokka — all perpetual. JetBrains' 2015 subscription switch caused a public revolt. |
| **Structure: one price + ~1 year of updates + paid major-version upgrades** | Beyond Compare, Fork, Quokka all use this. |
| **Honor-system / never-expiring trial works** and is offline-friendly | Sublime & Fork: full-featured free use with an occasional nag converts a meaningful fraction without anger. Critical for air-gapped/enterprise DBAs who can't phone home. |
| **Price cluster: $30–$100** | Beyond Compare $30/$60, Fork ~$50, Quokka $50/$100, Sublime $99. SSMS-specific: SSMS Tools Pack €25–€75, SSMSBoost ~$195. |
| **Pure free + donations is the failure mode** | ConEmu: $0/week, 0 patrons, project went dormant. Donations don't fund a real tool. |
| **Free-as-loss-leader only works with an ecosystem behind it** | Sourcetree → Atlassian, SQL Search → Redgate Toolbelt. SQL Pilot has no suite to funnel into, so it must charge directly. |

### Recommended plan

1. **Free tier (Apache-2.0, unchanged promise):** everything SQL Pilot does today + environment coloring + schema actions + settings UI + basic query history. This is the growth engine — never weaken it.

2. **SQL Pilot Pro (paid license key):** persistent/unlimited query history with full-text search, session/tab recovery, cross-server definition search, grid export + generate-INSERTs, snippet manager, "expand SELECT \*"/column-list generators.

3. **Price:** **~$39–$49 per user, perpetual**, including **1 year of updates**; major versions (v2, v3) are paid upgrades at a discount. Optional **Team license** (e.g. ~$199 for 10 seats) for orgs that want one invoice. Anchored slightly below SSMS Tools Pack/SSMSBoost to undercut on price while leading on SSMS-22 stability.

4. **Trial:** honor-system, **never-expiring**, full-featured Pro with an occasional unobtrusive nag — *not* a hard 30-day lockout. This matches Sublime/Fork, respects enterprise/air-gapped DBAs, and converts better than nagware-with-a-cliff.

5. **License mechanics (offline-first — mandatory for an in-SSMS extension):**
   - Issue a **signed license key** (e.g. Ed25519/RSA-signed payload containing name + edition + issue date). The extension validates the **signature locally** with an embedded public key — **no network call required** at runtime. This matters because SSMS extensions can't depend on heavy/blocking network access, and you already deliberately avoid runtime network dependencies.
   - You already have the hooks: `SqlPilotPackage.InitializeAsync()` is the natural place to load and verify the key; `SqlPilotSettings` is the natural place to store it; the existing update-checker/notification UI is the natural place to surface "upgrade to Pro" and license entry.
   - Sell via a low-overhead store (Gumroad/Lemon Squeezy/Paddle act as merchant-of-record and handle EU VAT) so you don't build billing.

6. **Optional secondary revenue (don't rely on it):** a **GitHub Sponsors** button (natively supported, one line in `package.json`) for goodwill and small recurring income — typically $500–$2k/mo for popular extensions, but the paid Pro tier is what actually funds development.

### Pitfalls to avoid

- **Don't relicense the existing free code or take features away from it.** The backlashes that hurt projects came from *removing* freedoms: HashiCorp/Terraform BSL, Akka, ElasticSearch, and (in this repo's own history) FluentAssertions v8's switch to a paid license. Open-core *adds* paid features alongside an untouched free core — that's the trustworthy version. (This is also why the repo pins FluentAssertions < 8.0 — the same principle applies to your own users.)
- **Don't go subscription-first.** Developers resist it; perpetual-with-update-window is the indie norm and avoids the JetBrains-style revolt.
- **Don't kill an existing free feature to make it Pro.** Quest doing this to ApexSQL's free tier generated lasting resentment. Only *new* features go behind the paywall.
- **Don't require a phone-home license server.** Offline signature verification only; many SQL Server shops are locked down.
- **Don't bet the roadmap on autocomplete.** It's the temptation (highest demand) but the wrong fight for a solo tool — see Tier C.

---

## Appendix — Competitor snapshot

| Tool | Model | Price | Relevance |
|---|---|---|---|
| SSMS Tools Pack | Paid, perpetual (solo dev) | €25–€75 | **Closest comparable**; the free→paid template |
| SSMSBoost | Freemium (120-day expiring free) | Pro ~$195 | Tab recovery, coloring, history |
| Redgate SQL Prompt | Subscription-first | ~$168–$369/user/yr | Heavyweight autocomplete/format leader |
| dbForge SQL Complete | Freemium (free Express) | $130–$400 | Autocomplete/format/refactor |
| ApexSQL Complete (Quest) | Was free → now paid | ~$500 | Cautionary: killing free tier = resentment |
| Redgate SQL Search | Free (loss-leader) | $0 | Free funnel into paid suite |
| SQL Hunting Dog | Free / OSS | $0 | Direct overlap, **stale since ~2016** |
| ssms-schema-folders | Free / OSS | $0 | **Broken on SSMS 22** |
| AxialSqlTools | Free / OSS (Apache 2.0) | $0 | New, SSMS-22-native — watch this one |

**Strategic read:** the free object-search/navigation niche SQL Pilot occupies is currently held by **stale or broken** incumbents on the newest SSMS. Ship a stable SSMS-22 experience, add query-history + coloring as free wedges, and monetize the power-user layer with a $39–49 perpetual Pro license on an honor-system trial.

*Note: a few prices (SSMSBoost ~$195, ApexSQL ~$500, SSMS Tools Pack tiers) are from third-party/older listings and should be re-verified against live vendor pages before being quoted externally.*
