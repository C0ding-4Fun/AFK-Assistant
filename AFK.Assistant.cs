//  Semi AFK Assistant
//  Gives audible warnings if the drones are idle, the drones are attacked, you are attacked or someone new arrives in local.
//  Gives audible warning if ore hold is open and full
using Parse = Sanderling.Parse;
using MemoryStruct = Sanderling.Interface.MemoryStruct;

//User configurations - Start
int EnterOffloadOreHoldFillPercent = 1;          // Percentage of ore hold fill level at which to beep
int DefenseEnterHitpointThresholdPercent = 70;    // Warn if shields less than this figure
string droneName = "Caldari Navy Wasp";           // Only monitor these drones for "Idle"
int maxDroneShield = 1000;                        // Sound alarm if drone shields drop below this figure. 1000 = 100%
bool pullDrones = false;                          // Should drones return to bay when alarm is sounded
//User configurations - End

//Any measuring functions
Sanderling.Parse.IMemoryMeasurement Measurement => Sanderling?.MemoryMeasurementParsed?.Value;

//Drone Code
IWindowDroneView WindowDrones => Measurement?.WindowDroneView?.FirstOrDefault();
DroneViewEntryGroup DronesInSpaceListEntry => WindowDrones?.ListView?.Entry?.OfType<DroneViewEntryGroup>()?.FirstOrDefault(Entry => null != Entry?.Caption?.Text?.RegexMatchIfSuccess(@"Drones in Local Space", RegexOptions.IgnoreCase));
DroneViewEntryItem[] AllDrones => WindowDrones?.ListView?.Entry?.OfType<DroneViewEntryItem>()?.ToArray();
int? DronesInSpaceCount => DronesInSpaceListEntry?.Caption?.Text?.AsDroneLabel()?.Status?.TryParseInt();
IEnumerable<Parse.IMenu> Menu => Measurement?.Menu;

//Local Window Code
IWindow SelectedTab => Measurement?.WindowStack?.FirstOrDefault()?.TabSelectedWindow;
IUIElementText[] allLocalChat => SelectedTab.LabelText?.ToArray();
int safeLocalCount = -1;

//Ship Code
bool Docked => (Measurement?.IsDocked ?? false);
bool Docking => Measurement?.ShipUi?.Indication?.LabelText?.Any(indicationLabel => (indicationLabel?.Text).RegexMatchSuccessIgnoreCase("docking")) ?? false;
bool Warping => Measurement?.ShipUi?.Indication?.LabelText?.Any(indicationLabel => (indicationLabel?.Text).RegexMatchSuccessIgnoreCase("warp")) ?? false;
Parse.IShipUi ShipUi => Measurement?.ShipUi;
int? ShieldHpPercent => ShipUi?.HitpointsAndEnergy?.Shield / 10;
bool UnderAttack => !(DefenseEnterHitpointThresholdPercent < ShieldHpPercent);

//Ore Code
Sanderling.Parse.IWindowInventory WindowInventory => Measurement?.WindowInventory?.FirstOrDefault(Entry => null != Entry?.Caption?.RegexMatchIfSuccess(@"Inventory", RegexOptions.IgnoreCase));
ITreeViewEntry InventoryActiveShipOreHold => WindowInventory?.ActiveShipEntry?.TreeEntryFromCargoSpaceType(ShipCargoSpaceTypeEnum.OreHold);
IInventoryCapacityGauge OreHoldCapacityMilli => WindowInventory?.SelectedRightInventoryCapacityMilli;
int? OreHoldFillPercent => (int?)((OreHoldCapacityMilli?.Used * 100) / OreHoldCapacityMilli?.Max);

for(;;)
{
  Sanderling.InvalidateMeasurement();
  if(string.Equals(GetCurrentStatus(), "In Space"))
  {
    if(!IsLocalSelected())
    {
      Host.Log("Local is not open");
      DoWarningBeep();
    }
    else if(HasLocalCountChanged())
    {
      Host.Log("Local Count has changed");
      DronesReturnToBay();
      DoAlarm();
    }

    if(AreDronesIdle())
    {
      Host.Log("Drones Idle");
      DoWarningBeep();
    }
    
    if(AreDronesDamaged())
    {
      Host.Log("Drones Damaged");
      DronesReturnToBay();
      DoAlarm();
    }
    
    if(UnderAttack)
    {
      Host.Log("Under Attack");
      DronesReturnToBay();
      DoAlarm();
    }
    
    if(IsOreHoldFull())
    {
      if(OreHoldFillPercent >= EnterOffloadOreHoldFillPercent)
      {
        Host.Log("Ore Hold Full");
        DronesReturnToBay();
        DoAlarm();
      }
    }
  }
  else
    Host.Log("Not in space");

  Host.Delay(5000);
}

bool IsLocalSelected()
{
  if(SelectedTab != null)
  {
    if(SelectedTab?.Caption?.RegexMatchSuccessIgnoreCase("local") ?? false)
      return true;
  }
  return false;
}

bool HasLocalCountChanged()
{
  int localCount = 0;
  foreach(UIElementText thisChat in allLocalChat)
  {
    if(!thisChat.Text.Contains("</color>"))
      localCount++;
  }

  if(safeLocalCount == -1)
    safeLocalCount = localCount;
  else
  {
    if(safeLocalCount != localCount)
      return true;
  }
  return false;
}

bool AreDronesIdle()
{
  if(getDroneCountInSpace() == 0)
    return false;

  foreach (DroneViewEntryItem drone in AllDrones)
  {
    if(drone?.LabelText?.FirstOrDefault()?.Text?.RegexMatchSuccessIgnoreCase(@droneName) ?? false)
    {
      if(drone?.LabelText?.FirstOrDefault()?.Text?.Contains("Idle") ?? false)
        return true;
    }
  }
  return false;
}

int getDroneCountInSpace()
{
  if(null != DronesInSpaceCount)
    return (int)DronesInSpaceCount;
  else
    return 0;
}

string GetCurrentStatus()
{
    var ManeuverType = Measurement?.ShipUi?.Indication?.ManeuverType;

    if (ShipManeuverTypeEnum.Warp == ManeuverType || Warping)
        return "Warping";
    else if (ShipManeuverTypeEnum.Jump == ManeuverType)
        return "Jumping";
    else if (Docked)
        return "Docked";
    else if (Docking)
        return "Docking";
    return "In Space";
}

void DronesReturnToBay()
{
  if(getDroneCountInSpace() > 0 && pullDrones)
  {
    Host.Log("Returning drones to bay");
    Sanderling.MouseClickRight(DronesInSpaceListEntry);
    Sanderling.MouseClickLeft(Menu?.FirstOrDefault()?.EntryFirstMatchingRegexPattern("return.*bay", RegexOptions.IgnoreCase));
  }
}

void DoWarningBeep()
{
  for(int i=0; i<3; i++)
    Console.Beep(1500, 200);
}

void DoAlarm()
{
  for(int i=0; i<5; i++)
  {
    Console.Beep(200, 200);
    Console.Beep(300, 200);
  }
}
  

bool AreDronesDamaged()
{
  if(getDroneCountInSpace() == 0)
    return false;
  
  foreach(DroneViewEntryItem drone in AllDrones)
  {
    if(drone?.LabelText?.FirstOrDefault()?.Text?.RegexMatchSuccessIgnoreCase(@droneName) ?? false)
    {
      string droneName = drone?.LabelText?.FirstOrDefault()?.Text;
      if(droneName != null && droneName.Contains("</color>"))   //Only check drones in space
      {
        int? droneShield = drone?.Hitpoints?.Shield;
        if (null != droneShield)
        {
          if (droneShield < maxDroneShield)
            return true;
        }
      }
    }  
  }
  return false;
}

bool IsOreHoldOpen()
{
  if ((InventoryActiveShipOreHold?.IsSelected ?? false))
    return true;
  return false;
}

bool IsOreHoldFull()
{
  if (IsOreHoldOpen())
  {
    if (OreHoldFillPercent >= EnterOffloadOreHoldFillPercent)
      return true;
  }
  return false;
}

