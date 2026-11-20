# Changelog

## 0.3.0
- Added **Area-level** general waybill destinations whenever multiple industries in an area share a captive loading or unloading cargo (e.g., `Sylva - Pulpwood` covering both Sylva Paperboard and Parson's Tannery, and `Alarka - Copper Ore` covering Alarka Copper Co. M1 and M2).
- Added support for general **unloading** destinations for industries with multiple captive unloading spurs for the same cargo.

## 0.2.2
- Updated compatibility for Railroader 0.6.3.
- Fixed destination overrides and Waybill Zone track picking for 0.6.3 API changes.

## 0.2.1
- Fixed a `NullReferenceException` in `Industry.get_Contract()` during overnight day rollover when at least one Waybill Zone is assigned to an Area.

## 0.2.0
- Added destination overrides in the Car Inspector, allowing players to override a car's active waybill destination.
- Added custom player-drawn Waybill Zones with the **Waybill Zones Manager** window to create, view, focus, and delete track zones as waybill destinations.
- Added fueling stand transfer loading, allowing empty freight cars to be waybilled to repair/service station fueling stands (such as coal towers and diesel fuel stands) to load fuel from station storage.

## 0.1.0
- Initial release.
- Added consolidated general waybill destinations for multi-landing and multi-spur industries (e.g., `Connelly Creek - Logs`, `Connelly Creek - Pulpwood`, `Walker Logging - Logs`, `Walker Logging - Pulpwood`).
