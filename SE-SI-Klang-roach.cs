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

// Set to true if you want unowned grinders to be renamed with the
// UNOWNED_GRINDER_TAG.
static readonly bool ENABLE_GRINDER_OWNERSHIP_FEATURE = true;

// How often to run an operation. Does not include player leaving cockpit
// check, that check is every tick. Helps reduce load on server.
static readonly int SECONDS_BETWEEN_RUN = 20;

// This tag will be added to the start of the name of any grinder that is not
// owned by the player.
static readonly string UNOWNED_GRINDER_TAG = "* ";

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
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
    }
}

private enum Operation {
    COUNT_TECH,
    CHECK_GRINDER_OWNERSHIP
}
Operation nextOperation = Operation.COUNT_TECH;
DateTime nextRunTime = DateTime.UtcNow;
public void Main() {
    string actionPerformed = null;

    if (ENABLE_SAFE_WELDER_FEATURE && PlayerLeftCockpit()) {
        TurnOffWelders();
        OutputLCD("Action: Turned welders off");
        return;
    }

    DateTime now = DateTime.UtcNow;
    if (now >= nextRunTime) {
        nextRunTime = now.AddSeconds(SECONDS_BETWEEN_RUN);
    } else {
        return;
    }

    if (nextOperation == Operation.COUNT_TECH) {
        nextOperation = Operation.CHECK_GRINDER_OWNERSHIP;
        if (ENABLE_TECH_COUNT_FEATURE) {
            CountTech();
            actionPerformed = "Action: Counted tech";
        } else {
            return;
        }
    } else if (nextOperation == Operation.CHECK_GRINDER_OWNERSHIP) {
        nextOperation = Operation.COUNT_TECH;
        if (ENABLE_GRINDER_OWNERSHIP_FEATURE) {
            CheckGrinderOwnership();
            actionPerformed = "Action: Checked grinder ownership";
        } else {
            return;
        }
    } else {
        throw new Exception($"Invalid operation {nextOperation}");
    }

    OutputLCD(actionPerformed);
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
    GridTerminalSystem.GetBlocksOfType(welders, welder => welder.IsSameConstructAs(Me));
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

private void CheckGrinderOwnership() {
    List<IMyShipGrinder> grinders = new List<IMyShipGrinder>();
    GridTerminalSystem.GetBlocksOfType(grinders, grinder => grinder.IsSameConstructAs(Me));

    bool grindersOn = false;
    List<IMyShipGrinder> offGrinders = new List<IMyShipGrinder>();

    foreach (IMyShipGrinder grinder in grinders) {
        if (grinder.Enabled) {
            grindersOn = true;
        } else {
            if (grinder.OwnerId == Me.OwnerId && grinder.IsFunctional) {
                offGrinders.Add(grinder);
            }
        }

        if (grinder.OwnerId != Me.OwnerId) {
            if (!grinder.CustomName.StartsWith(UNOWNED_GRINDER_TAG)) {
                grinder.CustomName = $"{UNOWNED_GRINDER_TAG}{grinder.CustomName}";
            }
        } else {
            if (grinder.CustomName.StartsWith(UNOWNED_GRINDER_TAG)) {
                grinder.CustomName = grinder.CustomName.Substring(UNOWNED_GRINDER_TAG.Length);
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
                lcdOutput.Append($"{type.SubtypeId}: {FormatNumber(techCount[type])}");
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

        outputPanel.WriteText(lcdOutput);
    }
}

private string FormatNumber(MyFixedPoint number) {
    if (number < 1000) {
        return $"{number}";
    } else if (number < 1000000) {
        return $"{Math.Round((double)number / 1000, 1)}K";
    } else {
        return $"{Math.Round((double)number / 1000000, 1)}M";
    }
}