# Car JSON for Forza Studio (format `gt-car-data`, version 1)

One JSON file per car, written by `gtdev car-json <DB0106.dat> <out dir> <label>...` (ROADMAP 9c). It holds:

- the car's stock values from the GT6 spec database (`gt6`);
- the FM4 `gamedb.slt` values built from them (`fm4`), ready to write into the target car's rows.

Forza Studio reads it, shows the values, and writes the FM4 rows. gt_dev never touches an FM4 database. How every
rule was found: `car_data.md`. Example files: `tests\car_json\*.json` (10 cars).

- Encoding: UTF-8 without BOM.
- Numbers: invariant culture.
- Missing values: `null` in `gt6`. In `fm4.fields` an entry with no value is not written at all.

## Top level

| key | type | content |
|---|---|---|
| `format` | string | always `"gt-car-data"` |
| `version` | int | `1`. A reader refuses a higher major version. |
| `source` | object | where the data comes from |
| `car` | object | identity, for display and for choosing the target car |
| `gt6` | object | the GT6 values (SI units unless the name says otherwise); information only |
| `fm4` | object | what to write into FM4: `fields`, `torqueCurve`, `paints` |

### `source`

| key | type | example | note |
|---|---|---|---|
| `game` | string | `"GT6"` | |
| `database` | string | `"DB0106.dat"` | spec database file name |
| `label` | string | `"supra_rz_97"` | GENERIC_CAR.CarLabel, also the file name |
| `genericCarId` | int | `82` | GENERIC_CAR.ID |
| `modelCode` | int | `40006` | GENERIC_CAR.ModelCode (car folder `car\MMMM\NNNN` in the viewer) |
| `exported` | string | `"2026-10-20"` | yyyy-MM-dd |

### `car`

| key | type | example | GT6 source |
|---|---|---|---|
| `name` | string | `"Toyota SUPRA RZ '97"` | CAR_NAME_ALPHABET.Name |
| `shortName` | string | `"SUPRA RZ '97"` | CAR_NAME_ALPHABET.ShortName |
| `maker` | string | `"toyota"` | CAR_VIEW.Tuner |
| `country` | string | `"jp"` | CAR_VIEW.CountryFlag |
| `year` | int | `1997` | GENERIC_CAR.Year |
| `drive` | string | `"FR"` | DRIVETRAIN.DriveTypeName: FF, FR, MR, RR, 4WD |

## `gt6`: the GT6 values

Shown by Forza Studio next to the FM4 values. Nothing here is written to FM4 directly. A value whose unit is not
known stays in GT6's own raw unit, and its name has no unit suffix.

| path | unit | GT6 source |
|---|---|---|
| `body.mass_kg` | kg | CHASSIS.mass |
| `body.frontWeight` | 0-1 | CHASSIS.percentageF / 1000 |
| `body.cgHeight_m` | m | CHASSIS.cgheight / 1000 |
| `body.length_m`, `width_m`, `height_m` | m | CHASSIS.length, width, height / 1000 |
| `body.wheelbase_m`, `trackFront_m`, `trackRear_m` | m | CHASSIS.wheelbase, treadF, treadR / 1000 |
| `body.cd` | - | CHASSIS.cd / 1000 |
| `body.clFront`, `clRear` | - | CHASSIS.clDFF, clDFR / 100 |
| `engine.type` | text | ENGINE.enginetype: L4, V8, Boxer4, W12, Rotor2... |
| `engine.cam` | text | ENGINE.cam |
| `engine.aspiration` | text | ENGINE.aspiration: NA, TURBO, SuperCharger |
| `engine.displacement_cc` | cc | ENGINE.displacement; rotary "654x2" = 1308 |
| `engine.idle_rpm` | rpm | ENGINE.idlerpm |
| `engine.redline_rpm` | rpm | ENGINE.RedLine x 100 |
| `engine.revLimit_rpm` | rpm | ENGINE.revlimit |
| `engine.power_ps`, `powerRpm` | PS, rpm | ENGINE.psvalue, psrpm (catalogue figure) |
| `engine.peakTorque_Nm`, `peakTorqueRpm` | N*m, rpm | the highest point of `torqueCurve` |
| `engine.torqueCurve[]` | `{rpm, Nm}` | ENGINE.torqueA..X (kgf*m x 100) at rpmA..X (x 100 rpm); points with torque 0 left out. Full boost included. |
| `engine.turbo` | raw | TURBINEKIT boost1/2, peakrpm1/2 (x 100 rpm), response1/2, wastegate. Only present for TURBO and SuperCharger. |
| `transmission.gears[]` | ratio | GEAR.Gear1st.. / 1000, up to the first 0 |
| `transmission.reverse`, `finalDrive` | ratio | GEAR.GearReverse, finalgearDF / 1000 |
| `transmission.flywheelInertia`, `wheelInertiaFront`, `wheelInertiaRear` | raw | DRIVETRAIN.iflywheel, iwheelF, iwheelR |
| `differential.front` / `rear` `.type`, `initial`, `accel`, `decel` | raw | LSD.difftypeF/R, LSDparamF/RDF, LSDparam2F/R, LSDparam3F/R |
| `suspension.front` / `rear` `.spring_kgf_mm` | kgf/mm | SUSPENSION.springrateDF[F/R] / 100 |
| `.leverRatio` | - | leverratioDF[F/R] / 1000 |
| `.wheelRate_N_mm` | N/mm | spring x lever^2 x 9.80665 |
| `.rideHeight_mm` | mm | rideheightDF[F/R] (GT6 reference point) |
| `.camber`, `.toe` | raw | camberDF[F/R], toeDF[F/R] |
| `.damperBump`, `.damperRebound`, `.damperBump2`, `.damperRebound2` | raw | dampF1B / F1R / F2B / F2R MIN[F/R] (first and second slope) |
| `.antiRoll` | raw | stabilizerMIN[F/R] |
| `.unsprungMass` | raw | unsprungmass[F/R] |
| `suspension.caster` | raw | caster |
| `brakes.torqueFront`, `torqueRear` | raw | BRAKE.BraketorqueF, BraketorqueR |
| `brakes.frontShare` | 0-1 | F / (F + R) |
| `brakes.tireMu` | raw | tireMuForBrake |
| `steering.lock_deg` | deg | STEER.steerlimit |
| `tyres.front` / `rear` `.width_mm`, `aspect`, `rim_in` | mm, %, inch | WHEEL.FrontWidth, FrontAspect, FrontDiameter / 10 (Rear*) |

## `fm4.fields`: single values

An array. Each entry is one FM4 column of one row:

| key | type | content |
|---|---|---|
| `table` | string | FM4 table name. `List_UpgradeEngineTurbo*` = the stock row of whichever turbo or supercharger table the target engine uses (`TurboSingle`, `TurboTwin`, `TurboQuad`, `CSC`, `DSC`). |
| `row` | string | which row of that table, see below |
| `column` | string | FM4 column name, exactly as in the database |
| `value` | number or string | the value to write. A string only for `kind` `lookup`. |
| `kind` | string | how sure the value is, see below |
| `gt6` | string | the GT6 field(s) and raw value it was made from (display only) |
| `rule` | string | the conversion, and how well it matched FM4 on the 9 compared cars (display only) |

### `row`: how to find the row in the target car

T = the FM4 car the data goes to (Data_Car.Id).

| `row` | tables | the row |
|---|---|---|
| `car` | Data_Car | `Id = T` |
| `engine` | Data_Engine | `EngineID = E`. E = List_UpgradeEngine.EngineID of the row with `Ordinal = T AND IsStock = 1`. |
| `stock` | List_UpgradeCarBodyWeight | `IsStock = 1 AND CarBodyId = T*1000` |
| `stock` | List_UpgradeBrakes | `Ordinal = T AND IsStock = 1` |
| `stock` | List_UpgradeDrivetrainTransmission | `DrivetrainID = DT AND IsStock = 1`. DT = List_UpgradeDrivetrain.DrivetrainID of the row with `Ordinal = T AND IsStock = 1` (Supra 214). **Not** the car id. |
| `stock` | List_UpgradeEngineDisplacement, Camshaft, Flywheel, Turbo* | `EngineID = E AND IsStock = 1` |
| `front` / `rear` | List_SpringDamperPhysics | the row the stock List_UpgradeSpringDamper (`Ordinal = T AND IsStock = 1`) points at by `FrontSpringDamperPhysicsID` / `RearSpringDamperPhysicsID` |
| `front` / `rear` | List_AntiSwayPhysics | the row the stock List_UpgradeAntiSwayFront / Rear (`Ordinal = T AND IsStock = 1`) points at by `AntiSwayPhysicsID` |

**Shared rows.** Engine rows (Data_Engine, List_UpgradeEngine*, List_TorqueCurve) belong to an EngineID. The Supra's
EngineID 215 is shared by 18 cars. Drivetrain rows can be shared too, and so can physics rows (presets). Before
writing a `row` other than `car`, Forza Studio counts how many cars use that id. If it is more than the target car, it
clones the rows to a new id first. Otherwise every car sharing them changes.

### `kind`

| kind | meaning | default action |
|---|---|---|
| `copy` | the same value in both games, only the unit changes. Checked equal on the 9 cars (exceptions are noted in `rule`). | write |
| `lookup` | an FM4 id to find by value. `CylinderID` `"Number=6;Rotors=0"` = the List_Cylinders row with `Number = 6 AND Rotors = 0`; there are two 3-cylinder rows (9 and 11), take the first. | find, then write |
| `estimate` | made from GT6 values with a rule that only approximates FM4 (see `rule`) | show it, the user ticks it before writing |
| `default` | FM4 uses the same value on all compared cars, whatever GT6 says | write only if the target row has no value yet |

FM4 columns that are not in the file are left as they are in the target car. That covers values Turn 10's tools
compute (Sim*, PerformanceIndex, ratings, ClassID, top speed and times), values measured on the model (Data_CarBody,
CarPartPositions, CameraOverrides), the other aero values, steering rates, and LSD (FM4 presets: see
`gt6.differential`). Forza Studio recomputes them or keeps them.

### Fields written in version 1

| table | row | columns |
|---|---|---|
| Data_Car | car | Year, CurbWeight, NumGears (forward), Displacement, DriveTypeID, EnginePlacementID, PowertrainID, EngineConfigID, CylinderID, AspirationTypeId, Front/RearTireWidthMM, Front/RearTireAspect, Front/RearWheelDiameterIN, BodyAeroLongitudinalDrag, WeightDistribution, SteerMaxAngle |
| List_UpgradeCarBodyWeight | stock | Mass, InitialMass, CMBackFront, CMHeight |
| List_UpgradeDrivetrainTransmission | stock | NumGears (forward + 1), GearRatio0 (= -reverse), GearRatio1..n, FinalDriveRatio |
| List_UpgradeEngineDisplacement | stock | Disp |
| List_UpgradeEngineCamshaft | stock | RedlineRPM, StallRPM, TorqueCurveMaxRPM, NumRPMEntriesArray |
| List_UpgradeEngineFlywheel | stock | MomentInertia |
| Data_Engine | engine | MomentInertia; StockBoost-bar (forced induction only) |
| List_UpgradeEngineTurbo* | stock | MaxScale (forced induction only) |
| List_SpringDamperPhysics | front, rear | DefSpringRate, DefDampenBumpRate, DefDampenReboundRate, StaticCamber, StaticToe, Caster |
| List_AntiSwayPhysics | front, rear | DefSwaybarStiffness |
| List_UpgradeBrakes | stock | FrontBrakeSize, RearBrakeSize, BrakeBiasSlider |

Transmission: after the last gear, every GearRatio column up to the end of the table (GearRatio8 in FM4) is set to
`-1`. The file only lists the gears used, so Forza Studio writes the `-1` values itself.

Upgrade rows (Level > 0) are not in version 1. FM4 builds them as the stock value times a factor, so Forza Studio can
rescale the target car's existing upgrades by new stock / old stock.

## `fm4.torqueCurve`: List_TorqueCurve

The full throttle curve of the stock camshaft, i.e. the List_TorqueCurve row with
`TorqueCurveID` = List_UpgradeEngineCamshaft(stock).TorqueCurveFullThrottleID.

| key | type | content |
|---|---|---|
| `kind` | string | `"estimate"` |
| `rule` | string | how it was built |
| `TorqueScale` | number | N*m: the peak of the curve without boost |
| `NumTorqueValues` | int | = Camshaft NumRPMEntriesArray = maxRpm / 100 + 1 |
| `maxRpm` | number | = Camshaft TorqueCurveMaxRPM |
| `v` | number[] | v0..v(N-2): one point every 100 rpm from 0, normalized (peak = 1) |

How FM4 stores it: the Supra row 215000 has TorqueScale 323.85, 86 values, peak `v36 = 1` at 3600 rpm. FM4 multiplies
the curve by the stock turbo MaxScale (1.39) to get 450 N*m. GT6's curve already includes the boost. The export
therefore divides by the same MaxScale it writes in `fm4.fields`, then normalizes.

- The **last value** v(N-1) is negative in FM4 (Supra -2.63; probably engine braking at the top). The file does not
  contain it. Forza Studio keeps the target row's last value and writes it at index N-1.
- The columns after the last value stay 0.
- Below the first GT6 point the curve is held flat. Above the last point it follows the last slope, and never goes
  below 0.
- At low rpm FM4's atmospheric curve is higher than GT6 / MaxScale, because FM4 ramps the boost from MinScale. Treat
  the curve as an estimate.

## `fm4.paints`: Combo_Colors

One entry per GT6 paint, in GT6 order (VARIATION.VarOrder).

| key | type | content | FM4 column |
|---|---|---|---|
| `Sequence` | int | 1.. | Combo_Colors.Sequence (row ID = T*1000 + Sequence, Ordinal = T) |
| `name` | string | VARIATION.NameEng | none (FM4 has no paint names) |
| `RGB` | int | 0x00RRGGBB. GT6 ColorChip is 0x00BBGGRR; the bytes are swapped here. | Combo_Colors.RGB |
| `hex` | string | `#RRGGBB`, for display | - |
| `Metallic` | 0 / 1 | 1 if the name contains Metallic, Mica or Pearl | Combo_Colors.Metallic |
| `metallicKind` | string | `"estimate"` | - |
| `default` | bool | GENERIC_CAR.MainColor | - |

Combo_Colors.BaseTexture, DamageTexture and ColorClassId are not in the file. The textures are `nodamage.tga` and
`damage.tga` on the Supra. For ColorClassId (List_ColorClass), Forza Studio takes the nearest class by colour, or
keeps the target car's value.

## Versions

- 1 (2026-10-20): first version. Stock values only.
