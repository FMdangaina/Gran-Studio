# Spec databases (car data)

## GT6: `specdb\GT6\DB0106.dat`

- SQLite 3 file encrypted with **Salsa20/20**, 256-bit key, nonce all zero, block counter from 0 (whole file, no
  header). Key `2D9EE83E63120EB25DF4981EE73C3BE194D0F059DE50C8D4FCB66C10D3EDC549` (from `scripts/gt6/SpecDatabaseUtil.ad`,
  published by Nenkai). `DB0105.dat` is the previous update; the highest number is the one to read.
- `database\gt6\menudb.dat` is SQLite with the same cipher and the menu key (`81CE2938...`, from `boot_functions.ad`).
- Checked 2026-10-18: both decrypt to `SQLite format 3` (`gtdev gt6-specdb`). 65 tables in DB0106.

Car tables (1227 rows each = the 1227 model folders of `car\`):

| table | key | what |
|---|---|---|
| GENERIC_CAR | CarLabel | ModelCode (maker * 10000 + car = `car\MMMM\CCCC`), Year, Price, PP, Power, Weight, Maker, Country, NumColor, flags (PremiumCar, OpenCar, Dirt, DLC...) |
| CAR_NAME_ALPHABET / _JAPAN | Label | Name, Grade, ShortName (1321 rows: also non-car labels) |
| VARIATION | Label + VarOrder | paint colours of the car: NameEng, ColorChip (RGB), CarColorID, AllPaintID -> PAINT_COLOR_INFO |
| MODEL_INFO | ModelCode | model size, driver / helmet / belt models, steering, crew |
| CAR_VIEW | Label | dealer sheet: drive type, displacement, power / torque rpm, size, mass |
| CAR_CUSTOM_INFO | Label | BitsA-F: the optional custom parts (custom_a..f groups of the model) |
| DEFAULT_PARTS | Label `df_pt_<car>` | row ID of each part table: ENGINE, CHASSIS, GEAR, DRIVETRAIN, SUSPENSION, BRAKE, LSD, tyres... |
| ENGINE | | displacement, layout, cam, aspiration, torque curve torqueA..X at rpmA..X, idle / rev limit / red line, soundNum |
| CHASSIS | | length, width, height, wheelbase, mass, weight split, cg height, cd, cl, ride height, gas capacity |
| GEAR / DRIVETRAIN / SUSPENSION / BRAKE / LSD / FRONTTIRE / REARTIRE / WHEEL | | the parts themselves |

Joins: GENERIC_CAR.Defaultparts = DEFAULT_PARTS.ID (1227 / 1227; the label `df_pt_<car>` does not always match, e.g.
`df_pt_sim__147_20_twinspark_02`), then DEFAULT_PARTS.ENGINE / CHASSIS = ENGINE.ID / CHASSIS.ID.

Units checked 2026-10-18 against real cars (Scion FR-S, Alfa 147 Twin Spark, 180SX, Isuzu 117) for the viewer's car
sheet (`Gt6SpecDb.CarSheet`, `gtdev car-list --sheet <root> <code>`):
- CAR_VIEW psvalue = PS, torquevalue = kgf*m x 100 (1850 = 18.5 kgf*m = 181 Nm), psrpm / torquerpm in rpm ("-" when
  unknown); 0 on some cars (FR-S torque, race cars): then the ENGINE curve gives them;
- ENGINE torqueA..X = kgf*m x 100 at rpmA..X x 100 (180SX peak 2800 at 40 = 28.0 kgf*m at 4000 rpm = its CAR_VIEW
  value); RedLine x 100 rpm, revlimit / shiftlimit in rpm; displacement cc or "-";
- CHASSIS length / width / height mm (CAR_VIEW dlength.. is 0 on 123 cars), percentageF = front weight per mille;
- WHEEL FrontDiameter = inch x 10, FrontWidth mm, FrontAspect %;
- WHEEL_EX (82 rows) = aftermarket wheels, ID = `carparts\hq\heNNN`, thumbnail `carparts\wheel_thumb\NNN_cc`.
  `carparts` also holds the custom wing parts `we` endplate / `wm` mount / `wp` plane / `ws` stay 001-005.
- GT5 cars: 1064 of 1088 find their GT6 row by model code or label (GT5 standard twins carry `_std`:
  `rx7_spiritr_a_02_std` = GT6 `rx7_spiritr_a_02`).

Still to check (item 9a): gear ratios (look x1000), cd / cl, suspension, tyres.

`gtdev sql <DB0106.dat | plain.sqlite> <query>` runs one query (the .dat is decrypted to the viewer's cache).

## GT5: `specdb\GT5_JP3009`

GTDB tables (`GtDb.cs`, re-implementation of PDTools.SpecDB): see `CarCatalog` for VARIATION / CAR_NAME_british /
GENERIC_CAR. Model folder code = VARIATION.ModelCode string.
