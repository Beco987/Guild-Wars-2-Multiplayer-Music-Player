; Oracle harness for the ORIGINAL host song pipeline (SongPlayer -> TrackWriter -> AddFiller ->
; CombineFlags -> TrackSharer/DelayToClock) from "Midi Player - Server\scripts\Functions.ahk", unmodified.
; Usage: AutoHotkeyU64.exe pipeline.ahk <caseDir> <outFile>
; caseDir contains Data.csv, Csv\<song>.csv and case.txt (key=value lines, see make_golden.py).
#NoEnv
StringCaseSense, On
SetBatchLines, -1
; ---- globals exactly as declared in GW2MMConnect.ahk / Connect.ahk ----
global Stop := false
global FromClient := false
global ClientData := ""
global OneWayTimes := []
global T1 := 0
global T2 := 0
global TickCounts := [0,0,0,0,0]
global SocketIDs := [1,0,0,0,0]
global Ip1GW2 := "", Ip2GW2 := "", Ip3GW2 := "", Ip4GW2 := "", Ip5GW2 := ""
global Pings := []
global SentLog := ""
ArrayIndex := 0, TempoIndex := 0, pzero = 0
Loop, 10
{
    Ip1%A_Index% := 0
    Ip2%A_Index% := 0
    Ip3%A_Index% := 0
    Ip4%A_Index% := 0
    Ip5%A_Index% := 0
}
; ---- Functions.ahk auto-execute initialisation (it is #Included at the bottom, so replicate it) ----
global DelayFiller := [], global TrackFiller := [], global tempotemp := 1, global DefaultTempo := 1
global TempoDuration := [], global Tempo := []
Loop, 5
{
	KeysToPlay%A_Index% := [], DelayToPlay%A_Index% := [], TimeToPlay%A_Index% := []
	latency%A_Index% := 0, SongIndex%A_Index% := 0, startTime%A_Index% := 0
	ClockTimes%A_Index% := [], MSecTimes%A_Index% := [], NewNotes%A_Index% := []
}
Loop, 8
{
	Track%A_Index% := []
	Track%A_Index%Duration := []
}
Gui, Add, Text,, oracle

caseDir := A_Args[1], outFile := A_Args[2]
SetWorkingDir, %caseDir%
OctaveDelay := ""
Loop, Read, case.txt
{
    p := InStr(A_LoopReadLine, "=")
    k := SubStr(A_LoopReadLine, 1, p - 1), v := SubStr(A_LoopReadLine, p + 1)
    if (k = "song")
        HighlightedSong := v
    else if (k = "octavedelay")
        OctaveDelay := v
    else if (k = "ip") {
        Loop, Parse, v, `,
            if (A_LoopField != "")
                Ip%A_LoopField% := 1
    } else if (k = "gw2") {
        Loop, Parse, v, `,
            Ip%A_Index%GW2 := A_LoopField
    } else if (k = "pings") {
        Loop, Parse, v, `,
            Pings[A_Index] := A_LoopField
    } else if (k = "ticks") {
        Loop, Parse, v, `,
            TickCounts[A_Index] := A_LoopField
    } else if (k = "sockets") {
        Loop, Parse, v, `,
            SocketIDs[A_Index] := A_LoopField
    } else if (k = "fromclient") {
        FromClient := (v = 1)
    } else if (k = "clienttempo") {
        DefaultTempo := v
    }
}
global Delay := OctaveDelay
if (FromClient)
{
    ; What the original client sent: its CSV read with "Loop, Read" (line breaks removed) and joined with `n.
    Loop, Read, Csv\%HighlightedSong%.csv
        ClientData .= A_LoopReadLine . "`n"
}
Stop := true   ; keeps the SongPlayer1 timer from dispatching keys after TrackSharer arms it
Gosub, SongPlayer

o := "tick1=" TickCounts[1] "`n"
Loop, 5
{
    o .= "notes" A_Index "=" EncNotes(NewNotes%A_Index%) "`n"
    o .= "delays" A_Index "=" Enc(DelayToPlay%A_Index%) "`n"
    o .= "times" A_Index "=" Enc(TimeToPlay%A_Index%) "`n"
    o .= "latency" A_Index "=" latency%A_Index% "`n"
}
o .= SentLog
FileDelete, %outFile%
FileAppend, %o%, %outFile%, UTF-8
ExitApp

Enc(a) {
    s := ""
    for k, v in a
        s .= (k = 1 ? "" : "|") v
    return a.Length() ":" s
}
EncNotes(a) {
    s := ""
    for k, v in a {
        e := ""
        Loop, Parse, v
            e .= (A_Index = 1 ? "" : ".") Ord(A_LoopField)
        s .= (k = 1 ? "" : "|") e
    }
    return a.Length() ":" s
}

#Include ..\..\..\Midi Player - Server\scripts\Functions.ahk
#Include ..\..\..\Midi Player - Server\scripts\ConnectButtons.ahk
#Include stubs.ahk
