# 🎯 Darts Practice Tracker

A personal skills development project built to track and analyze darts practice sessions. Developed as a self-directed learning exercise to explore **.NET MAUI Blazor Hybrid** application development — a stack I had no prior experience with before starting this project.

> **Note:** This app was conceived, designed, and built in a single day as a learning exercise. It is actively used for personal practice tracking and continues to be refined.

---

## Screenshots

### Home Screen
![Home Screen](ScreenShots/HomeScreen.JPG)

### Cricket In Play
![Cricket](ScreenShots/Cricket%20in%20play.JPG)

### 301 In Play
![301](ScreenShots/301%20in%20play.JPG)

---

## What it does

Tracks practice sessions across three darts games with per-dart granularity:

### 🏏 Cricket
- Unified input/scoreboard — each target row shows live chalk-style mark indicators alongside tap-to-score buttons
- Marks update live on every dart tap (`/` → `X` → `⊗`)
- SVG chalk-on-felt mark graphics rendered inline
- Tracks **ochres thrown** and **total marks** to complete the game

### 3️⃣0️⃣1️⃣ / 5️⃣0️⃣1️⃣ X01
- **SIDO** (Single In / Double Out) and **DIDO** (Double In / Double Out) mode selection
  - 301 defaults to DIDO, 501 defaults to SIDO
- Live **Left**, **Scored**, and **Out** counters update on every dart tap
- Automatic bust detection — disables number buttons and flips Miss → **Bust** mid-ochre
- Finishing double detected live — locks remaining dart slots and activates **✔ Finish** button
- Tracks **darts to double-in**, **avg score per ochre**, **time to double-out**, **total darts**, and **finishing dart** (1st, 2nd, or 3rd)

### Checkout Suggestions
- Inline checkout panel appears automatically when remaining score ≤ 170
- Shows up to 3 standard routes (T20/T19/D16 style) with triples in gold, doubles in blue
- Updates live with every dart thrown

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | .NET 10 MAUI Blazor Hybrid |
| Language | C# 12 |
| UI | Blazor components + CSS (dark felt theme) |
| Graphics | Inline SVG with CSS filters |
| Database | SQLite via sqlite-net-pcl |
| Platform | Windows (iOS architecture in place) |
| IDE | Visual Studio 2026 Community |

---

## Architecture

```
J1sDartSharp/
├── Models/              # Session models, DartThrow, stats POCOs
├── Data/                # SQLite table DTOs (DbModels.cs)
├── Services/            # DartsDatabase, SessionHistoryService, CheckoutService
├── Pages/               # Cricket.razor, X01.razor, History.razor, Index.razor
├── Shared/              # DartInputPad, ChalkMark, CheckoutPanel, MainLayout
├── Platforms/Windows/   # MAUI Windows bootstrapper
└── wwwroot/             # index.html, app.css
```

**Key design decisions:**
- `DartInputPad` is a fully reusable component shared across Cricket and X01 with a `TwoColumn` layout parameter
- `ChalkMark` renders SVG chalk-style score indicators (empty box → `/` → `X` → `⊗`) as a standalone Blazor component
- `CheckoutService` is a static lookup table covering all valid finishes from 2–170
- `SessionHistoryService` maintains an in-memory cache over a SQLite backend, loaded once at startup via `MainLayout`
- Bust detection, live scoring, and finish recognition all happen dart-by-dart via `OnDartAdded` callbacks — no waiting for Enter

---

## What I learned

This project was my first exposure to .NET MAUI and Blazor Hybrid development. Key learning areas:

- **MAUI project structure** — platform bootstrapping, `MauiProgram.cs`, `BlazorWebView` setup
- **Blazor component lifecycle** — `OnParametersSet`, `StateHasChanged`, parent/child callback patterns
- **Blazor gotchas** — `RenderFragment` closure capture issues, SVG namespace conflicts with Razor syntax, component keying (`@key`) for forced re-instantiation
- **SQLite in MAUI** — `sqlite-net-pcl`, async CRUD, `FileSystem.AppDataDirectory` for cross-platform paths
- **Touch-first UI design** — 52px minimum touch targets, `touch-action: manipulation` for zero tap delay, bust/finish state management

---

## Status & Roadmap

- ✅ Cricket — fully functional
- ✅ 301 / 501 — fully functional
- ✅ SQLite history and aggregate stats
- ✅ Windows (Surface Pro touchscreen tested)
- 🔲 iOS deployment (pending Mac build machine)
- 🔲 Match mode (multiple legs)
- 🔲 Multiplayer / opponent tracking

---

## Development Approach

This project was built using **AI-assisted development** with Anthropic's Claude as a
collaborative coding partner. Claude was used for code generation, architecture decisions,
and debugging — while all product decisions, UX feedback, and requirements came from me
as the developer.

This was not a "generate and accept" workflow. Throughout development I:
- Caught and corrected bad UX decisions before they shipped
- Made direct hands-on code modifications to meet exact specifications
- Debugged build and runtime errors independently
- Drove all game logic requirements from domain knowledge as an active darts player

The ability to effectively direct, evaluate, iterate with, and where necessary override
AI tools is increasingly a core developer skill. This project demonstrates that full
workflow in practice — from initial architecture through real-world testing and refinement.

---

## About this project

I've been a competitive darts player in a local league for several years. During a period of career transition I decided to build tools I actually use rather than follow tutorials. This app is used at real practice sessions and continues to evolve based on real feedback from actual use.

*Built by a senior .NET developer (C#, ASP.NET MVC, SQL Server) expanding into mobile/hybrid development.*