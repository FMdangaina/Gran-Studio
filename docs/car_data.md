# GT6 car data -> FM4 gamedb (ROADMAP 9c)

What the GT6 spec database (`specdb\GT6\DB0106.dat`, see `specdb.md`) holds about a car, and where it goes in FM4's
`Media\db\gamedb.slt`. Checked on 9 cars that are in both games (2026-10-19, `tests\car_data\compare.txt`):
Supra RZ (GT6 `supra_rz_97` = FM4 461 `TOY_SupraRZ_98`), NSX-R '92, S2000 '03, RX-7 Spirit R '02, RX-7 FC '90,
Lancer Evo VI GSR, Evo VIII MR, Impreza 22B, Corvette Z06 '06.

Tools (read only): `gtdev car-data gt6 <db> <label> <out>` (every row of the car: default parts, tyres, paints, all
its upgrade parts), `gtdev car-data fm4 <gamedb.slt> <id> <out>` (every row of the car in FM4),
`gtdev car-data cmp <gt6 db> <gamedb.slt> <out> label=id...` (side by side with the FM4 / GT6 ratio).

## How the two databases are built

- **GT6**: GENERIC_CAR (CarLabel, ID) -> DEFAULT_PARTS (one row id per part table: ENGINE, CHASSIS, GEAR,
  SUSPENSION, BRAKE, LSD, DRIVETRAIN, TURBINEKIT, STEER, FRONTTIRE / REARTIRE...). Part rows have `UseCar` = the car's
  GENERIC_CAR.ID and `category` = upgrade stage (0 stock): the upgrades of a car are the rows with its UseCar.
  Integers with fixed scales (x100, x1000, kgf, "level" units).
- **FM4**: Data_Car (Id) -> List_Upgrade* (Id = car*1000 + Level, IsStock = 1 for the stock part) -> physics rows
  (List_SpringDamperPhysics / List_AntiSwayPhysics, rear = +100). The drivetrain parts hang on the stock
  **List_UpgradeDrivetrain.DrivetrainID** (Supra 214), not on the car id; the engine parts on EngineID (Supra 215,
  shared by 18 cars). Real numbers in SI or US units.
- The FM4 DB in `F:\Xenia\Games\FM4_Extracted` still holds creation-tool test rows on the Supra: transmission,
  clutch, differential, driveline rows 461000-461003 with DrivetrainID 461 (unused: the Supra points at 214).

## Same value (copy, unit change only)

| quantity | GT6 | FM4 | check |
|---|---|---|---|
| mass | CHASSIS.mass kg | Data_Car.CurbWeight = kg / 100; List_UpgradeCarBodyWeight.Mass / InitialMass kg | 6 / 9 exact (others: another version of the car) |
| wheelbase | CHASSIS.wheelbase mm | Data_CarBody.ModelWheelbase m (measured on the model) | +-1% |
| displacement | ENGINE.displacement cc (rotary "654x2") | Data_Car.Displacement, List_UpgradeEngineDisplacement.Disp | 9 / 9 |
| gear ratios | GEAR.Gear1st..11th / 1000 | List_UpgradeDrivetrainTransmission.GearRatio1..N; GearRatio0 = -reverse; NumGears = forward + 1; unused = -1 | 9 / 9 exact |
| reverse | GEAR.GearReverse / 1000 | -GearRatio0 | 8 / 9 |
| final drive | GEAR.finalgearDF / 1000 | FinalDriveRatio | 7 / 9 (other versions) |
| red line | ENGINE.RedLine x 100 rpm | List_UpgradeEngineCamshaft.RedlineRPM | 7 / 9 |
| idle | ENGINE.idlerpm | Camshaft.StallRPM (FM4 800 on almost every car) | |
| peak torque | ENGINE torqueA..X (kgf*m x 100) at rpmA..X (x 100 rpm) | List_TorqueCurve v0..vN (normalized, N points to TorqueCurveMaxRPM) x TorqueScale (N*m), x stock turbo / supercharger MaxScale | Supra 451 / 450 Nm, NSX 294 / 294, S2000 218 / 219, Z06 636 / 637 |
| tyre sizes | WHEEL Front/RearWidth mm, Aspect %, Diameter inch x 10 | Data_Car Front/RearTireWidthMM, TireAspect, WheelDiameterIN; List_UpgradeCarBodyTireWidth*, RimSize* | Supra exact |
| paints | VARIATION.ColorChip 0x00BBGGRR, NameEng | Combo_Colors.RGB 0x00RRGGBB, Metallic, ColorClassId | Supra Super Red IV 113 0 9 / 117 5 8 |
| year, maker, country | GENERIC_CAR.Year, Maker, Country | Data_Car.Year, MakeID (List_CarMake), CountryID | ids differ: map by name |
| drive / layout | DRIVETRAIN.DriveTypeName FR/FF/4WD/MR/RR | Data_Car.DriveTypeID, EnginePlacementID, PowertrainID | |
| engine type | ENGINE.enginetype L6/V8/R2, cam, aspiration NA/TURBO/SC | Data_Car.EngineConfigID, CylinderID, AspirationTypeId; Data_Engine | |

GT6 torque is the whole engine at full boost; FM4 keeps an atmospheric curve and multiplies it by the turbo
(MaxScale 1.39 on the Supra, ramping from MinScale at low rpm): to build an FM4 curve, divide the GT6 curve by the
chosen MaxScale.

## Same quantity, other scale or model (needs a rule)

| quantity | GT6 | FM4 | finding |
|---|---|---|---|
| front weight | CHASSIS.percentageF / 1000 | Data_Car.WeightDistribution, CarBodyWeight.CMBackFront | 3 / 9 equal; GT6 rounds (Supra 0.60 vs 0.53, real 0.53) |
| cg height | CHASSIS.cgheight mm | CarBodyWeight.CMHeight m | +-9% |
| drag | CHASSIS.cd / 1000 (0.30) | Data_Car.BodyAeroLongitudinalDrag = drag force in **lbf at 100 mph** | 0.5 x 1.225 x Cd x A x 44.7^2 / 4.448 with A = 0.85 x width x height: 0.83-1.03 |
| steering lock | STEER.steerlimit deg (40) | SteerMaxAngle (42 on all 9) | FM4 constant |
| springs | SUSPENSION.springrateDFF / 100 kgf/mm, leverratioDFF / 1000 | SpringDamperPhysics.DefSpringRate (N/mm at the wheel?) | FM4 = 1.1-1.7 x GT6 wheel rate (k x lever^2): no fixed scale |
| anti-roll | stabilizerMINF / MINR (level) | AntiSwayPhysics.DefSwaybarStiffness | FM4 uses presets (7.44, 9.24, 11.36 / 4.96, 6.16, 7.57) ~ 0.06 x GT6 |
| dampers | dampF1B / F1R (bump / rebound), F2 = second slope | DefDampenBumpRate / DefDampenReboundRate | ~ GT6 / 600, spread 2x |
| ride height | rideheightDFF mm (body to ground, GT6 reference) | DefRideHeight m (another reference) | no fixed scale |
| brakes | BRAKE.BraketorqueF / R (torque), tireMuForBrake | List_UpgradeBrakes FrontBrakeSize in, CaliperPistons, GameFrictionScaleBraking; BrakeBiasSlider 0.5 | size ~ BraketorqueF / 330 |
| LSD | LSD difftype, LSDparam (initial), 2 (accel), 3 (decel) | Differential *LimitedSlipTorqueAccel / Decel N*m | FM4 presets (850 / 106, 700 / 0, 1000 / 125) |
| turbo | TURBINEKIT boost1/2, peakrpm, response, wastegate | Data_Engine.StockBoost-bar, TurboSingle / Twin MaxScale, MinScale, PowerMaxScale | boost2 x 0.13 = bar on 2 cars |
| flywheel | DRIVETRAIN.iflywheel | List_UpgradeEngineFlywheel.MomentInertia kg*m^2 | ~ GT6 / 1000, spread 2x |
| camber / toe / caster | SUSPENSION camberDFF, toeDFR, caster | StaticCamber -0.5, StaticToe 0, Caster 5 on all 9 | FM4 defaults |

## Only in one game

- GT6 only: the tyre model (TIRECOMPOUND curves, TIREFORCEVOL), steering assist graphs, ABS / TCS / ASCC levels,
  unsprung mass, wheel inertias, the upgrade stages with their own values per car.
- FM4 only, computed by Turn 10's tools (do not copy, let the editor recompute or keep a donor's): Sim* (peak
  power, 0-60, top speed...), PerformanceIndex, ratings, ClassID; aero lateral / vertical drag, downforce, tyre aero
  hack; FixListing*; steering rates. From the model: Data_CarBody (wheelbase, tracks, bounding box),
  CarPartPositions, CameraOverrides.

## Export

`gtdev car-json` writes the rules above as one JSON per car, layout in `car_json.md`. The import into the gamedb
belongs to Forza Studio.

FM4 id tables used by the export (FM4 gamedb):

- List_DriveType: 1 FWD, 2 RWD, 3 AWD.
- List_EnginePlacement: 1 front, 2 mid, 3 rear.
- Powertrains: Id 0 FWD front; 1, 2, 3 RWD front / mid / rear; 4, 5, 6 AWD front / mid / rear.
- List_EngineConfig: 1 V, 2 W, 3 I, 4 Rotary, 5 F (flat).
- List_Cylinders: Number + Rotors (8 = 2 rotors).
- List_Aspiration: 1 natural, 2 turbo, 3 twin turbo, 5 DSC, 6 CSC.

GT6 4WD cars with CHASSIS.EngineMount 3 are mid-engined (R8, Gallardo, 205 T16, RS200).
