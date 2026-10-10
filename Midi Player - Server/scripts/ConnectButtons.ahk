portMapping:
	Gui,PortMappingGui:Show,w320 h350, Port Mapping
return

; Determines the input delay for all participating clients using auto feedback to determine the time it takes from:
; Server -> Client -> Guild Wars 2 -> Server
Sync:
	global SocketIDs
	global Pings := [0,0,0,0,0]
	for i, v in SocketIDs
	{
		if (v != 0)
		{
			inputDelay := []
			audioMeter := VA_GetAudioMeter()

			VA_IAudioMeterInformation_GetMeteringChannelCount(audioMeter, channelCount)

			; "The peak value for each channel is recorded over one device
			;  period and made available during the subsequent device period."
			VA_GetDevicePeriod("capture", devicePeriod)
			Sleep, 150
			; Sync Host
			if (v == 1)
			{
				Msgbox, "Open Gw2, then press OK"
				Loop, 10
				{
					Send, 1
					StartTick := A_TickCount

					Loop
					{
						VA_IAudioMeterInformation_GetPeakValue(audioMeter, peak)
						if (peak > 0.001)
						{
							delay := A_TickCount - StartTick
							inputDelay.Push(delay)
							break
						}
						Sleep, 1
					}
					Sleep, 4000 
				}
			}
			; Sync Clients
			Else
			{
				sText := "Sync_Instrument"
				SendTextToClient(sText, v)
			}
			sum := 0.0
			for index, value in inputDelay
				sum += value

			count := inputDelay.Length()
			mean := Round(sum / count)
			Pings[i] := mean
			varname := "Ip" . A_Index . "GW2"
    		%varname% = %mean%
			GuiControl, PortMappingGui:, %varName%, %mean%
		}
	}
return

Ping:
	global SocketIDs
	global RTTPings
	global T1
	for i, v in SocketIDs
	{
		RTTPings.Push("0")
		if (v != 0 && v != 1)
		{
			Loop, 50
			{
				T1 := A_TickCount
				sText := "Ping_Server"
				SendTextToClient(sText, v)
				Sleep, 1000
			}
			sum := 0
			cleaned := RemoveOutliers(OneWayTimes)
			for i, v in cleaned
				sum += v
			sum /= cleaned.Length()
			sum := Floor(sum)
			varName := "Ip" . A_Index . "GW2Ping"
			if (sum = "")
				sum := 0
			%varname% = %sum%
			GuiControl, PortMappingGui:, %varName%, %sum%
			RTTPings.Push(sum)
		}
	}
return

PortMappingGuiSaveButton:
	Gui, Submit, Nohide
	Gui, PortMappingGui:Submit
	global Pings := [] 
	Loop, 5
    {
        varName := "Ip" . A_Index . "GW2"
		varName := Round(varName)
        ; read the control value directly from the GUI
        GuiControlGet, val, , %varName%, PortMappingGui
        Pings.Push(val)
    }
return

; Function to remove outliers using IQR method
RemoveOutliers(arr, multiplier := 1.5) {
    if (arr.Length() < 4)
        return arr
    
    ; Create a sorted copy
    sorted := []
    for index, value in arr
        sorted.Push(value)
    
    ; Bubble sort
    Loop, % sorted.Length()
    {
        swapped := false
        Loop, % sorted.Length() - A_Index
        {
            if (sorted[A_Index] > sorted[A_Index + 1]) {
                temp := sorted[A_Index]
                sorted[A_Index] := sorted[A_Index + 1]
                sorted[A_Index + 1] := temp
                swapped := true
            }
        }
        if (!swapped)
            break
    }
    
    ; Calculate Q1 and Q3
    n := sorted.Length()
    Q1 := Percentile(sorted, 25)
    Q3 := Percentile(sorted, 75)
    IQR := Q3 - Q1
    
    ; Calculate bounds
    lowerBound := Q1 - (multiplier * IQR)
    upperBound := Q3 + (multiplier * IQR)
    
    ; Filter outliers
    result := []
    for index, value in arr {
        if (value >= lowerBound && value <= upperBound)
            result.Push(value)
    }
    
    return result
}

; Calculate percentile
Percentile(sorted, p) {
    n := sorted.Length()
    pos := (p / 100) * (n + 1)
    
    if (pos <= 1)
        return sorted[1]
    if (pos >= n)
        return sorted[n]
    
    lower := Floor(pos)
    upper := Ceil(pos)
    
    if (lower = upper)
        return sorted[lower]
    
    ; Linear interpolation
    fraction := pos - lower
    return sorted[lower] + fraction * (sorted[upper] - sorted[lower])
}

; Helper function to display array as string
ArrayToString(arr) {
    str := ""
    for index, value in arr
        str .= value (index < arr.Length() ? ", " : "")
    return str
}