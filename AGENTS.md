# Agent Guidelines & Developer Reference

This document serves as the primary orientation and operating guide for AI agents working in this repository.

---

## 1. Game Codebase & Inspection Protocol

### Decompiled Game Source
* **Path:** `D:\Development\Railroader\Railroader_Decompiled`
* **Rule:** Inspect APIs, data structures, and game behaviors directly by reading the decompiled C# source files using native tools (`read_file` or search commands). Never run reflection scripts to inspect game APIs.

### Base Game Reference Data
* **Path:** `D:\Development\Railroader\Railroader_Data_Reference`
* **Rule:** Consult reference documentation in `Railroader_Data_Reference` (e.g. `industries.md`, `commodities.md`, `cars.md`) for industry spurs, loads, and car types.

---

## 2. Mod Scope & Core Problem

* **Problem Statement:**
  Multi-landing / multi-spur industries like Connelly Creek (`connelly0`) and Walker Logging (`logcamp1`) require cars to be addressed to individual landing tracks (e.g., Connelly Creek L1, L2, or L3). If an empty car waybilled to L1 is spotted at L2, L2 rejects the waybill and refuses to load.
* **Solution:**
  1. Add a general waybill destination option per cargo type (e.g., `Connelly Creek - Logs`, `Connelly Creek - Pulpwood`) in the Auto-Destination menus (`When Empty` / `When Loaded`).
  2. Individual landings remain available for players who want specific routing.
  3. When an empty car carrying a general waybill arrives at any landing matching that industry and cargo, the landing accepts the car, completes/services the waybill, and loads the car.

---

## 3. Build & Deployment Lifecycle

* **Mod Project File:** `src/GeneralWaybilling.csproj`
* **Active Mods Deployment Target:** `D:\Documents\Railroader\Mods\CRZBP.GeneralWaybilling`

### Standard Commands
* **Build (Release):**
  ```powershell
  dotnet build src/GeneralWaybilling.csproj -c Release
  ```
* **Build & Deploy to Game:**
  ```powershell
  dotnet build src/GeneralWaybilling.csproj -c Release -t:Test
  ```
* **Package Release Zip:**
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\Build-ReleaseZip.ps1
  ```

---

## 4. Coding & Architecture Rules

* **NO `var` Keyword:** Explicit types are strictly enforced across all C# source files per `.editorconfig`.
* **Domain Encapsulation:** Pass rich game objects (`IndustryComponent`, `OpsCarPosition`, `Car`, `Load`) rather than extracting primitive strings.
* **Native Tools First:** Use designated native file tools (`read_file`, `write_file`, `edit_file`) rather than shell redirection operators.
