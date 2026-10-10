#NoEnv
#MaxHotkeysPerInterval 99000000
#HotkeyInterval 99000000
#KeyHistory 0
#singleinstance force
ListLines Off
Process, Priority, , R
SetBatchLines, -1
SetKeyDelay, 0
SetMouseDelay, -1
SetDefaultMouseSpeed, 0
SetWinDelay, -1
SetControlDelay, -1
StringCaseSense, On
DllCall("winmm\timeBeginPeriod", "UInt", 1)

Delay = 240
count = 1
TrackHolder := [], Notes := [], Durations := [], Volume := [], VolumesDif := [], Delay := [], ToPlay := [], KeyState := [], Tempo := [], TempoDuration := [], IsNotNote := [], DelayToPlay := [], Octaves := [], global OctaveSheet := [], ArrayIndex := 0, TempoIndex := 0, DefaultTempo = 1, pzero = 0
trackcount = 1, songLength = 0, HighlightedSong := ""
DisplayName := ""
EnableClicks := 4
HasSelecetedFiled := false
Playing := 0
global OneWayTime
global Stop := false
global AccName

Connected := true

; refresh checkboxes
Loop, 10
{
    Ip1%A_Index% := 0
    Ip2%A_Index% := 0
    Ip3%A_Index% := 0
    Ip4%A_Index% := 0
    Ip5%A_Index% := 0
}

; Prepare keysets for each player
global Dispatch := {}  
bases    := [191,207,223,239,255,271,287,303]  ; ascii keysets for each player
windows  := ["Player1","Player2","Player3","Player4","Player5","Player6","Player7","Player8"]
keys     := ["1","F1","2","F2","3","4","F3","5","F4","6","F5","7","8", "9", "0"]

; Populate for each base + offset
for i, base in bases {
    playerWin := windows[i]
    for offset, key in keys {
        ch := Chr(base + offset)
        Dispatch[ch] := {win: playerWin, key: key}
    }
}

;Connect config
Data := []
Loop, read, ConnectConfig.txt
{
    Loop, parse, A_LoopReadLine, `,
    {
        Data.Push(A_LoopField)
    }
}
DataLen := Data.Length()
Loop %DataLen%
    {
        if (Data[A_Index] == "Name")
            {
                if (Data[A_Index+1] == 0)
                    {
                        InputBox, Name1, Peer hostname or IP address, Please enter your peer's hostname or IP address:,,, 100,,,,, Enter Name Here
                        Data2 := []
                        Loop, read, ConnectConfig.txt
                            {
                                Data2.Push(A_LoopReadLine)
                            }
                        DataLen := Data2.Length()
                        FileDelete, ConnectConfig.txt
                        toAppend := ""
                        Loop %DataLen%
                            {
                                entry := Data2[A_Index]
                                if (InStr(entry,"Name"))
                                {
                                    newline := "Name," Name1 "`n"
                                    toAppend .= newline
                                }
                            }
                        FileAppend, %toAppend%, ConnectConfig.txt
                    }
                else
                    {
                        Name1 := Data[A_Index+1]
                    }
            }	
    }

;Read Config to get the Octave Delay variable
Data := []
Loop, read, Config.txt
{
    Loop, parse, A_LoopReadLine, `,
    {
        Data.Push(A_LoopField)
    }
}
DataLen := Data.Length()
Loop %DataLen%
    {
        if (Data[A_Index] == "OctaveDelay")
            {
                OctaveDelay := Data[A_Index+1]
            }	
        if (Data[A_Index] == "AlwaysOnTop")
            {
                AlwaysOnTop := Data[A_Index+1]
            }	
        if (Data[A_Index] == "IsTransparent")
            {
                IsTransparent := Data[A_Index+1]
            }	
        if (Data[A_Index] == "Ping")
            {
                OneWayTime := Data[A_Index+1]
            }	
    }
global Delay := OctaveDelay
global Delay2 := Ceil(Delay/10)
global TransparentEnabled := IsTransparent
global OnTopEnabled := AlwaysOnTop

#Include, scripts/ConnectGui.ahk

Gosub, ReloadSongs

#include, scripts/Connect.ahk

#Include, scripts/Controls.ahk

#Include, scripts/Buttons.ahk

#Include, scripts/Functions.ahk

#Include, scripts/ConnectFunctions.ahk

#Include, scripts/AHKsock.ahk

#Include, scripts/VA.ahk

GuiClose:
;So that we don't go back to listening on disconnect
bExiting := True
DllCall("winmm\timeEndPeriod", "UInt", 1)
    
/*! If the GUI is closed, this function will be called twice:
    - Once non-critically when the GUI event fires (GuiEscape or GuiClose) (graceful shutdown will occur), and
    - Once more critically during the OnExit sub (after the previous GUI event calls ExitApp)
    
    But if the application is exited using the Exit item in the tray's menu, graceful shutdown will be impossible
    because AHKsock_Close() will only be called once critically during the OnExit sub.
*/
AHKsock_Close()
ExitApp