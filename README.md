# General Waybilling for Railroader

**General Waybilling** simplifies car routing for multi-track and multi-landing industries in *Railroader* (such as Connelly Creek and Walker Logging).

---

## The Problem

In vanilla *Railroader*, industries with multiple landing spurs for the same commodity (e.g. Connelly Creek's log landings `L1`, `L2`, `L3` and pulpwood landings `P1`, `P2`, `P3`) treat each spur as a separate destination.

When waybilling cars (e.g. via automatic waybills under **When Empty**):
* You must assign each car to a specific landing (e.g., `Connelly Creek: L1`).
* If you shove a cut of cars into `L2` or `L3`, those landings reject the cars because their waybills specify `L1`. The cars sit idle on the track without loading until manually edited.

## The Solution

**General Waybilling** automatically creates consolidated, general destination options for any industry with multiple loaders of the same cargo:

* **New Consolidated Options:**
  * `Connelly Creek - Logs`
  * `Connelly Creek - Pulpwood`
  * `Walker Logging - Logs`
  * `Walker Logging - Pulpwood`
* **Flexible Landing Acceptance:**
  When a car with a general waybill (such as `Connelly Creek - Logs`) arrives on **any** matching landing track (`L1`, `L2`, or `L3`), that landing immediately accepts the car, services/completes the waybill, and loads it.
* **Full Compatibility:**
  * Specific landing destinations (`L1`, `L2`, etc.) remain fully available for players who still want precise track assignments.
  * Works seamlessly with Auto-Destination menus, "Copy to Coupled", and "Cycle Waybills".
  * Highlights all member landing tracks when clicking the destination location in the Car Inspector.

---

## Installation

1. Ensure **Allow Unsafe DLL Mods** is enabled in *Railroader* (**Preferences -> Features -> Experimental**).
2. Download or compile `GeneralWaybilling`.
3. Extract the mod folder to:
   ```text
   %USERPROFILE%\Documents\Railroader\Mods\CRZBP.GeneralWaybilling\
   ```
4. Verify that `GeneralWaybilling.dll`, `0Harmony.dll`, and `Info.json` reside directly inside that folder.

---

## Building from Source

```powershell
# Build in Release mode:
dotnet build src/GeneralWaybilling.csproj -c Release

# Build and deploy directly to the game's active mods folder:
dotnet build src/GeneralWaybilling.csproj -c Release -t:Test

# Generate release zip package:
powershell -ExecutionPolicy Bypass -File .\Build-ReleaseZip.ps1
```
