// SE-SI-Klang-roach

/*
MIT License

Copyright (c) 2025 David Payne

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

// The following actions will require a script re-compile:
//
//  1) Naming the output LCD panel.
//  2) Building a cockpit.

// Output will be sent to an LCD that contains this string. This LCD must exist
// with this name before the script is compiled. If the script is already
// running when the LCD is named then re-compile the script.
static readonly string OUTPUT_GPS_TOKEN = "[ROACH]";

// Set to true if you want the scirpt to count tech2x, tech4x and tech8x.
static readonly bool ENABLE_TECH_COUNT_FEATURE = true;

// Set to true if you want your welders to shut off when you leave the cockpit.
static readonly bool ENABLE_SAFE_WELDER_FEATURE = true;

// Set to true if you want unowned blocks to be renamed with the
// UNOWNED_BLOCK_TAG.
static readonly bool ENABLE_BLOCK_OWNERSHIP_FEATURE = true;

// This tag will be added to the start of the name of any block that is not
// owned by the player.
static readonly string UNOWNED_BLOCK_TAG = "* ";

// How often to run an operation. Does not include player leaving cockpit
// check, that check is every tick. Helps reduce load on server.
static readonly int SECONDS_BETWEEN_RUN = 10;

// Set to true if you want shield information to be displayed on the LCD.
static readonly bool ENABLE_SHIELD_OUTPUT_FEATURE = true;

// Set to the name of your shield controller and shield modulator. Required for
// SHIELD_OUTPUT_FEATURE.
static readonly string SHIELD_MODULATOR_BLOCK_NAME = "[A] Shield Modulator";
static readonly string SHIELD_CONTROLLER_BLOCK_NAME = "[A] Shield Controller";

// Set to true if you want the build and repair status to be output to the LCD.
static readonly bool ENBALE_BAR_STATUS_FEATURE = true;

/////////////////////////////////////////////////////
// End of configuration, no changes past this point.
/////////////////////////////////////////////////////

IMyTextPanel outputPanel = null;

Dictionary<MyItemType, MyFixedPoint> techCount = new Dictionary<MyItemType, MyFixedPoint> {
    {
        new MyItemType("MyObjectBuilder_Component", "Tech2x"),
        0
    },
    {
        new MyItemType("MyObjectBuilder_Component", "Tech4x"),
        0
    },
    {
        new MyItemType("MyObjectBuilder_Component", "Tech8x"),
        0
    }
};
List<MyItemType> techTypes;

List<IMyCockpit> cockpits = new List<IMyCockpit>();

IMyTerminalBlock shieldModulator = null;
IMyTerminalBlock shieldController = null;
Func<IMyTerminalBlock, float> getShieldPercent = null;
Func<IMyTerminalBlock, float> getShieldCharge = null;

IMyShipWelder buildAndRepair = null;

bool updateBlockStatus = false;
public Program() {
    techTypes = new List<MyItemType>(techCount.Keys);

    List<IMyTextPanel> panels = new List<IMyTextPanel>();
    GridTerminalSystem.GetBlocksOfType(panels, block => block.IsSameConstructAs(Me));
    foreach (IMyTextPanel panel in panels) {
        if (panel.CustomName.Contains(OUTPUT_GPS_TOKEN)) {
            outputPanel = panel;
            outputPanel.ContentType = ContentType.TEXT_AND_IMAGE;
            break;
        }
    }

    if (outputPanel == null) {
        throw new Exception("No output panel found");
    }

    if (ENABLE_SAFE_WELDER_FEATURE) {
        GridTerminalSystem.GetBlocksOfType(cockpits, cockpit => cockpit.IsSameConstructAs(Me));
    }

    if (ENABLE_SHIELD_OUTPUT_FEATURE) {
        shieldModulator = GridTerminalSystem.GetBlockWithName(SHIELD_MODULATOR_BLOCK_NAME);
        shieldController = GridTerminalSystem.GetBlockWithName(SHIELD_CONTROLLER_BLOCK_NAME);

        if (shieldController != null) {
            ITerminalProperty<IReadOnlyDictionary<string, Delegate>> apiProperty = Me.GetProperty("DefenseSystemsPbAPI")
                .As<IReadOnlyDictionary<string, Delegate>>();
            if (apiProperty != null) {
                IReadOnlyDictionary<string, Delegate> api = apiProperty.GetValue(Me);
                getShieldPercent = (Func<IMyTerminalBlock, float>)api["GetShieldPercent"];
                getShieldCharge = (Func<IMyTerminalBlock, float>)api["GetCharge"];
            }
        }
    }

    if (ENBALE_BAR_STATUS_FEATURE || ENABLE_SAFE_WELDER_FEATURE) {
        List<IMyShipWelder> welders = new List<IMyShipWelder>();
        GridTerminalSystem.GetBlocksOfType(welders,
            welder => welder.IsSameConstructAs(Me));
        foreach (IMyShipWelder welder in welders) {
            if (welder.DefinitionDisplayNameText == "BuildAndRepairSystem") {
                buildAndRepair = welder;
                break;
            }
        }
    }

    updateBlockStatus = ENABLE_SHIELD_OUTPUT_FEATURE || ENBALE_BAR_STATUS_FEATURE;

    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

DateTime updateShieldAfter = DateTime.UtcNow;

private enum Operation {
    COUNT_TECH,
    CHECK_BLOCK_OWNERSHIP
}
Operation nextOperation = Operation.COUNT_TECH;
DateTime nextRunTime = DateTime.UtcNow;
public void Main() {
    string actionPerformed;

    if (ENABLE_SAFE_WELDER_FEATURE && PlayerLeftCockpit()) {
        TurnOffWelders();
        OutputLCD("Action: Turned welders off");
        return;
    }

    DateTime now = DateTime.UtcNow;

    if (updateBlockStatus && now > updateShieldAfter) {
        updateShieldAfter = now.AddSeconds(1);
        bool statusChanged = UpdateBlockStatus();
        if (statusChanged) {
            OutputLCD("Action: Updated Block Status");
        }
        return;
    }

    if (now >= nextRunTime) {
        nextRunTime = now.AddSeconds(SECONDS_BETWEEN_RUN);
    } else {
        return;
    }

    if (nextOperation == Operation.COUNT_TECH) {
        nextOperation = Operation.CHECK_BLOCK_OWNERSHIP;
        if (ENABLE_TECH_COUNT_FEATURE) {
            CountTech();
            actionPerformed = "Action: Counted tech";
        } else {
            return;
        }
    } else if (nextOperation == Operation.CHECK_BLOCK_OWNERSHIP) {
        nextOperation = Operation.COUNT_TECH;
        if (ENABLE_BLOCK_OWNERSHIP_FEATURE) {
            CheckBlockOwnership();
            actionPerformed = "Action: Checked block ownership";
        } else {
            return;
        }
    } else {
        throw new Exception($"Invalid operation {nextOperation}");
    }

    OutputLCD(actionPerformed);
}

string oldShieldStatus = "";
string shieldStatus = "\n";
private bool UpdateBlockStatus() {
    StringBuilder statusBuilder = new StringBuilder();

    if (ENABLE_SHIELD_OUTPUT_FEATURE) {
        UpdateShieldStatus(statusBuilder);
    }

    if (ENBALE_BAR_STATUS_FEATURE) {
        UpdateBARStatus(statusBuilder);
    }

    shieldStatus = statusBuilder.ToString();
    bool statusChanged = oldShieldStatus != shieldStatus;
    oldShieldStatus = shieldStatus;
    return statusChanged;
}

private void UpdateShieldStatus(StringBuilder statusBuilder) {
    if (shieldModulator == null) {
        statusBuilder.Append("Shield modulator not found\n");
        return;
    }

    if (shieldController == null) {
        statusBuilder.Append("Shield controller not found\n");
        return;
    }

    bool entitiesMayPass = shieldModulator.GetValue<bool>("DS-M_ModulateGrids");
    bool shieldFortified = shieldController.GetValue<Boolean>("DS-C_ShieldFortify");

    if (!entitiesMayPass && !shieldFortified) {
        statusBuilder.Append("Shield: Normal\n");
    } else if (entitiesMayPass && !shieldFortified) {
        statusBuilder.Append("Shield: Entities may pass\n");
    } else if (!entitiesMayPass && shieldFortified) {
        statusBuilder.Append("Shield: Fortified\n");
    } else {
        statusBuilder.Append("Shield: Fortified, Entities may pass\n");
    }

    statusBuilder.Append($"{GetShieldPercent()} {GetShieldCharge()}\n");
}

private void UpdateBARStatus(StringBuilder statusBuilder) {
    if (buildAndRepair == null) {
        statusBuilder.Append("BAR: Not found\n");
        return;
    }

    if (buildAndRepair.Enabled) {
        statusBuilder.Append("BAR: On\n");
    } else {
        statusBuilder.Append("BAR: Off\n");
    }
}

string GetShieldPercent() {
    if (getShieldPercent == null || shieldController == null) {
        return "Failed to get shield percent\n";
    }

    float shieldPercent = getShieldPercent.Invoke(shieldController);

    return $"{Math.Round(shieldPercent, 1)}%";
}

string GetShieldCharge() {
    if (getShieldPercent == null || shieldController == null) {
        return "Failed to get shield charge\n";
    }

    float shieldCharge = getShieldCharge.Invoke(shieldController);

    return FormatNumber(shieldCharge * 100);
}

bool lastPlayerInCockpit = true;
private bool PlayerLeftCockpit() {
    bool playerInCockpit = false;
    foreach (IMyCockpit cockpit in cockpits) {
        if (cockpit.IsUnderControl) {
            playerInCockpit = true;
            break;
        }
    }

    bool playerLeftCockpit = lastPlayerInCockpit && !playerInCockpit;

    bool output = false;
    if (lastPlayerInCockpit != playerInCockpit) {
        output = true;
    }

    lastPlayerInCockpit = playerInCockpit;

    if (output) {
        if (playerInCockpit) {
            OutputLCD($"Action: Player entered cockpit");
        } else {
            OutputLCD($"Action: Player left cockpit");
        }
    }

    return playerLeftCockpit;
}

private void TurnOffWelders() {
    List<IMyShipWelder> welders = new List<IMyShipWelder>();
    GridTerminalSystem.GetBlocksOfType(welders,
        welder => welder.IsSameConstructAs(Me) && welder != buildAndRepair);
    foreach (IMyShipWelder welder in welders) {
        welder.Enabled = false;
    }
}

private void CountTech() {
    foreach (MyItemType type in techTypes) {
        techCount[type] = 0;
    }

    List<IMyInventory> inventories = GetInventories();
    foreach (IMyInventory inventory in inventories) {
        List<MyInventoryItem> items = new List<MyInventoryItem>();
        inventory.GetItems(items, item => techCount.ContainsKey(item.Type));
        foreach (MyInventoryItem item in items) {
            techCount[item.Type] += item.Amount;
        }
    }
}

private List<IMyInventory> GetInventories() {
    List<IMyInventory> inventories = new List<IMyInventory>();
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks, block =>
        block.HasInventory && block.IsSameConstructAs(Me));
    foreach (IMyTerminalBlock block in blocks) {
        inventories.Add(block.GetInventory());
    }
    return inventories;
}

private void CheckBlockOwnership() {
    List<IMyFunctionalBlock> blocks = new List<IMyFunctionalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks,
        block => block.IsSameConstructAs(Me));

    bool grindersOn = false;
    List<IMyShipGrinder> offGrinders = new List<IMyShipGrinder>();

    foreach (IMyFunctionalBlock block in blocks) {
        if (block is IMyShipGrinder) {
            if (block.Enabled) {
                grindersOn = true;
            } else {
                if (block.OwnerId == Me.OwnerId && block.IsFunctional) {
                    offGrinders.Add((IMyShipGrinder)block);
                }
            }
        }

        if (block.OwnerId != Me.OwnerId) {
            if (!block.CustomName.StartsWith(UNOWNED_BLOCK_TAG)) {
                block.CustomName = $"{UNOWNED_BLOCK_TAG}{block.CustomName}";
            }
        } else {
            if (block.CustomName.StartsWith(UNOWNED_BLOCK_TAG)) {
                block.CustomName = block.CustomName.Substring(UNOWNED_BLOCK_TAG.Length);
            }
        }
    }

    if (grindersOn) {
        foreach (IMyShipGrinder offGrinder in offGrinders) {
            offGrinder.Enabled = true;
        }
    }
}

private void OutputLCD(string actionPerformed) {
    if (outputPanel != null) {
        StringBuilder lcdOutput = new StringBuilder($"{DateTime.UtcNow}\n");

        if (actionPerformed != null) {
            lcdOutput.Append($"{actionPerformed}\n");
        }

        if (ENABLE_TECH_COUNT_FEATURE) {
            bool first = true;
            foreach (MyItemType type in techCount.Keys) {
                if (first) {
                    first = false;
                } else {
                    lcdOutput.Append(", ");
                }
                lcdOutput.Append($"{type.SubtypeId}: {FormatNumber((double)techCount[type])}");
            }
            lcdOutput.Append("\n");
        }

        if (ENABLE_SAFE_WELDER_FEATURE) {
            if (lastPlayerInCockpit) {
                lcdOutput.Append("In cockpit\n");
            } else {
                lcdOutput.Append("Out of cockpit\n");
            }
        }

        if (ENABLE_SHIELD_OUTPUT_FEATURE) {
            lcdOutput.Append(shieldStatus);
        }

        outputPanel.WriteText(lcdOutput);
    }
}

private string FormatNumber(double number) {
    if (number < 1000) {
        return $"{number}";
    } else if (number < 1000000) {
        return $"{Math.Round(number / 1000, 1)}K";
    } else {
        return $"{Math.Round(number / 1000000, 1)}M";
    }
}