#SingleInstance Force
#Persistent
#NoEnv
; Song-writing variables
global DelayFiller := []
global TrackFiller := []
global tempotemp := 1
global DefaultTempo := 1
global TempoDuration := []
global Tempo := []
global startMSec := 0
global nextKey := ""
global tick1 := true
global latency1, global latency2, global latency3, global latency4, global latency5
global latencyHalf1, global latencyHalf2, global latencyHalf3, global latencyHalf4, global latencyHalf5
global startTime1, global startTime2, global startTime3, global startTime4, global startTime5

Loop, 5
{
	KeysToPlay%A_Index% := []
	DelayToPlay%A_Index% := []
	TimeToPlay%A_Index% := []
	latency%A_Index% := 0
	SongIndex%A_Index% := 0
	startTime%A_Index% := 0
	ClockTimes%A_Index% := []
	MSecTimes%A_Index% := []
	NewNotes%A_Index% := []
	NewNotesHalf%A_Index% := []
	DelayToPlayHalf%A_Index% := []
	latency%A_Index% := 0
	latencyHalf%A_Index% := 0
	start%A_Index% := false
}
Loop, 8
{
	Track%A_Index% := []
	Track%A_Index%Duration := []
}

;------------------------------------------------------------------------
;----------------------- REFRESH LIST VIEW CONTENTS ---------------------
;------------------------------------------------------------------------
ReloadSongs:
	; This cross references file names in the csv folder with file names in the data file
	; With get the file date from the csv folder and the number of players from the data file
	GuiControl, Show, MidiPlayer
	Gui, 1:Default
	LV_Delete()
	Data := []
	Loop, read, Data.csv
	{
		Loop, parse, A_LoopReadLine, `,
		{
			Data.Push(A_LoopField)
		}
	}

	Loop, Csv\*.*
	{
		SplitPath, A_LoopFileName, OutFileName,, OutExtension
		NoExt := StrReplace(A_LoopFileName, ".csv" , "")
		DataLen := Data.Length()
		Loop %DataLen%
		{
			if (Data[A_Index] == noExt)
			{
				FormatTime, FileDate, %A_LoopFileTimeCreated%, yyyy-MM-dd
				LV_Add("", noExt, Data[A_Index+1], FileDate)
				LV_ModifyCol(2, "Integer")
			}
		}
	}
return

;------- Remake New Song GUI ------;
NewNewSongGUI:
	Gui,NewSong:Add,Text,x0 y3 w50 h20,Title :
	Gui,NewSong:Add,Edit,x30 y0 w170 h20 vNewSongTitle,
	Gui,NewSong:Add,Text,x0 y20 w100 h20,Players :
	Gui,NewSong:Add,Button,x80 y20 w20 h20 gPlayersExplanation,?
	Gui,NewSong:Add,DropDownList,x100 y20 w100 h100 vNumberPlayers, 1||2|3|4|5|6|7|8
	Gui,NewSong:Add,Text,x0 y40 w100 h20,Tempo Modifier :
	Gui,NewSong:Add,DropDownList,x100 y40 w100 h100 vTempoModifier, -10|-9|-8|-7|-6|-5|-4|-3|-2|-1|0||1|2|3|4|5|6|7|8|9|10
	Gui,NewSong:Add,Button,x80 y40 w20 h20 gTempoExplanation,?
	Gui,NewSong:Add,Text,x0 y75 w100 h20,Description :
	Gui,NewSong:Add,Button,x80 y75 w20 h20 gDescriptionExplanation,?
	Gui,NewSong:Add,Edit,x100 y60 w100 h50 vSongDescription,
	Gui,NewSong:Add,Button,x0 y110 w50 h50 gFileSelectButton,Import Midi
	Gui,NewSong:Add,Button,x70 y160 w75 h40 gNewSongOkButton Center,Add song
return

;------------------------------------------------------------------------
;---------------------- LISTVIEW CLICK HANDLER --------------------------
;------------------------------------------------------------------------
MyListView:
Critical ;Fixes known issue with Autohotkey GuiEvent Listener
	if A_GuiEvent = I
	{
		LV_GetText(RowText, A_EventInfo) ; Get the text from the row's first field.
		HighlightedSong := RowText
		Data := []
		Loop, read, Data.csv
		{
			Loop, parse, A_LoopReadLine, `,
			{
				Data.Push(A_LoopField)
			}
		}
		DataLen := Data.Length()
		Loop %DataLen%
		{
			if (Data[A_Index] == RowText)
			{
				GuiControl,, SongDescription, % Data[A_Index+3]
			}
		}
	}
return

;------------------------------------------------------------------------
;---------------------- REFRESH PLAYER OCTAVES --------------------------
;------------------------------------------------------------------------
RefreshPlayers:
	Sleep, 150
	ControlSend,, {9}, Player1
	ControlSend,, {9}, Player2
	ControlSend,, {9}, Player3
	ControlSend,, {9}, Player4
	ControlSend,, {9}, Player5
	Sleep, 150
	ControlSend,, {9}, Player1
	ControlSend,, {9}, Player2
	ControlSend,, {9}, Player3
	ControlSend,, {9}, Player4
	ControlSend,, {9}, Player5
	Sleep, 150
	ControlSend,, {0}, Player1
	ControlSend,, {0}, Player2
	ControlSend,, {0}, Player3
	ControlSend,, {0}, Player4
	ControlSend,, {0}, Player5
return

;------------------------------------------------------------------------
;----------------------- MAIN SONG PLAYER -------------------------------
;------------------------------------------------------------------------
SongPlayer:
	Gui, Submit, Nohide
	; Look... this was before I learned about nesting arrays using Array[index] := []
	global KeysToPlay1 := [],  global KeysToPlay2 := [],  global KeysToPlay3 := [],  global KeysToPlay4 := [], global KeysToPlay5 := []
	global DelayToPlay1 := [], global DelayToPlay2 := [], global DelayToPlay3 := [], global DelayToPlay4 := [], global DelayToPlay5 := []
	global TimeToPlay1 := [], global TimeToPlay2 := [], global TimeToPlay3 := [], global TimeToPlay4 := [], global TimeToPlay5 := []
	global SongIndex1 := 1, global SongIndex2 := 1, global SongIndex3 := 1, global SongIndex4 := 1, global SongIndex5 := 1
	global startTime1 := 0, global startTime2 := 0, global startTime3 := 0, global startTime4 := 0, global startTime5 := 0 
	global NewNotes1 := [], global NewNotes2 := [], global NewNotes3 := [], global NewNotes4 := [], global NewNotes5 := []
	global DelayToPlayHalf1 := [], global DelayToPlayHalf2 := [], global DelayToPlayHalf3 := [], global DelayToPlayHalf4 := [], global DelayToPlayHalf5 := []
	global NewNotesHalf1 := [], global NewNotesHalf2 := [], global NewNotesHalf3 := [], global NewNotesHalf4 := [], global NewNotesHalf5 := []
	global ClockTimes1 := [], global ClockTimes2 := [], global ClockTimes3 := [], global ClockTimes4 := [], global ClockTimes5 := []
	global start1 := false, global start2 := false, global start3 := false, global start4 := false, global start5 := false
	global latency1 := 0, global latency2 := 0, global latency3 := 0, global latency4 := 0, global latency5 := 0
	global latencyHalf1 := 0, global latencyHalf2 := 0, global latencyHalf3 := 0, global latencyHalf4 := 0, global latencyHalf5 := 0
	
	Data := []
	global DefaultTempo
	global ClientData

	; Make adjustments to Default Tempo based on data from Data.csv
	; Default Tempo uses New Song tempo modifier (-2|-1|0|1|2 etc) to make playback faster or slower
	if (!FromClient)
	{
		DefaultTempo := 1
		Loop, read, Data.csv
		{
			Loop, parse, A_LoopReadLine, `,
			{
				Data.Push(A_LoopField)
			}
		}
		DataLen := Data.Length()
		Loop %DataLen%
		{
			if (Data[A_Index] == HighlightedSong)
				DefaultTempo += (Data[A_Index+2] * 0.05)*-1
		}
	}

	; Initialize arrays for parsing MIDI CSV
	Tempo := [], TempoDuration := [], IsNotNote := [], DelayToPlay := [], Volumes := [], Durations := [], Notes := [], TrackHolder := [], KeyState := []
	postnotes := false, trackcounter = 0, pzero := 0

	; Initialize associative arrays for track-based events
	Loop, %ArrayIndex%
	{
		Volume%ArrayIndex% := ""
		Durations%ArrayIndex% := ""
		Notes%ArrayIndex% := ""
		TrackHolder%ArrayIndex% := ""
		KeyState%ArrayIndex% := ""
	}
	ArrayIndex = 1
	
	; Read through Csv\SongName.csv, extract note‑on events & tempo changes
	; Csv events are formatted like so : Track (Number), timestamp, Note_on_c OR Tempo change, velocity, midi key index
	if (!FromClient)
	{
		Loop, read, Csv/%HighlightedSong%.csv
		{
			subIndex = 0
			record := false
			tempoer := false
			Loop, parse, A_LoopReadLine, `,
			{
				subIndex++
				var = %A_Loopfield%
				if(subIndex = 1){
					track = %var%
				}
				if(subIndex = 2){
					duration = %var%
				}
				if (subIndex = 3 && var = "Note_on_c"){
					record := true
				}
				if (subIndex = 3 && var = "Tempo"){
					tempoer := true
				}
				; Sharps = ZXVBNASFGHQWRTY
				if (subIndex = 5) {
					if(var = 48){
						note = z
					}
					if (var = 49){
						note = Z
					}
					if(var = 50){
						note = x
					}
					if(var = 51){
						note = X
					}
					if(var = 52){
						note = c
					}
					if(var = 53){
						note = v
					}
					if(var = 54)
					{
						note = V
					}
					if(var = 55){
						note = b
					}
					if(var = 56)
					{
						note = B
					}
					if(var = 57){
						note = n
					}
					if(var = 58){
						note = N
					}
					if(var = 59){
						note = m
					}
					if(var = 60){
						note = a
					}
					if(var = 61){
						note = A
					}
					if(var = 62){
						note = s
					}
					if(var = 63){
						note = S
					}
					if(var = 64){
						note = d
					}
					if(var = 65){
						note = f
					}
					if(var = 66){
						note = F
					}
					if(var = 67){
						note = g
					}
					if(var = 68){
						note = G
					}
					if(var = 69){
						note = h
					}
					if(var = 70){
						note = H
					}
					if(var = 71){
						note = j
					}
					if(var = 72){
						note = q
					}
					if(var = 73){
						note = Q
					}
					if(var = 74){
						note = w
					}
					if(var = 75){
						note = W
					}
					if(var = 76){
						note = e
					}
					if(var = 77){
						note = r
					}
					if(var = 78){
						note = R
					}
					if(var = 79){
						note = t
					}
					if(var = 80){
						note = T
					}
					if(var = 81){
						note = y
					}
					if(var = 82){
						note = Y
					}
					if(var = 83){
						note = u
					}
					if(var = 84){
						note = i
					}
					; Swap up/down keys
					if(var = 96){
						note = 9
					}
					if(var = 97){
						note = 0
					}
				}
				; identifies if a csv event changes song tempo
				if (subIndex = 4 && tempoer = true){
					tempy = %var%
					if((postnotes = false && pzero = 0) || (postnotes = true)){
						outTemp := "B"
						outTemp .= tempy
						Tempo.Push(outTemp)
						TempoDuration.Push(duration)
						IsNotNote.Push(" ")
						TempoIndex++
						prevDuration := duration
						pzero = 1
					}
				}
				; otherwise, add a note event
				if(subIndex = 6 && record = true && var > 0){
					postnotes := true
					var2 := Round(var, -1)
					Volume%ArrayIndex% := var2
					Durations%ArrayIndex% := duration
					Notes%ArrayIndex% := note
					TrackHolder%ArrayIndex% := track
					KeyState%ArrayIndex% := keyState
					ArrayIndex++
				}
			}
		}
	}
	Else
	{
		Loop, parse, ClientData, `n
		{
			subIndex = 0
			record := false
			tempoer := false
			Loop, parse, A_Loopfield, `,
			{
				subIndex++
				var = %A_Loopfield%
				if(subIndex = 1){
					track = %var%
				}
				if(subIndex = 2){
					duration = %var%
				}
				if (subIndex = 3 && var = "Note_on_c"){
					record := true
				}
				if (subIndex = 3 && var = "Tempo"){
					tempoer := true
				}
				; Sharps = ZXVBNASFGHQWRTY
				if (subIndex = 5) {
					if(var = 48){
						note = z
					}
					if (var = 49){
						note = Z
					}
					if(var = 50){
						note = x
					}
					if(var = 51){
						note = X
					}
					if(var = 52){
						note = c
					}
					if(var = 53){
						note = v
					}
					if(var = 54)
					{
						note = V
					}
					if(var = 55){
						note = b
					}
					if(var = 56)
					{
						note = B
					}
					if(var = 57){
						note = n
					}
					if(var = 58){
						note = N
					}
					if(var = 59){
						note = m
					}
					if(var = 60){
						note = a
					}
					if(var = 61){
						note = A
					}
					if(var = 62){
						note = s
					}
					if(var = 63){
						note = S
					}
					if(var = 64){
						note = d
					}
					if(var = 65){
						note = f
					}
					if(var = 66){
						note = F
					}
					if(var = 67){
						note = g
					}
					if(var = 68){
						note = G
					}
					if(var = 69){
						note = h
					}
					if(var = 70){
						note = H
					}
					if(var = 71){
						note = j
					}
					if(var = 72){
						note = q
					}
					if(var = 73){
						note = Q
					}
					if(var = 74){
						note = w
					}
					if(var = 75){
						note = W
					}
					if(var = 76){
						note = e
					}
					if(var = 77){
						note = r
					}
					if(var = 78){
						note = R
					}
					if(var = 79){
						note = t
					}
					if(var = 80){
						note = T
					}
					if(var = 81){
						note = y
					}
					if(var = 82){
						note = Y
					}
					if(var = 83){
						note = u
					}
					if(var = 84){
						note = i
					}
					if(var = 84){
						note = i
					}
					; Swap up/down keys
					if(var = 96){
						note = 9
					}
					if(var = 97){
						note = 0
					}
				}
				; identifies if a csv event changes song tempo
				if (subIndex = 4 && tempoer = true){
					tempy = %var%
					if((postnotes = false && pzero = 0) || (postnotes = true)){
						outTemp := "B"
						outTemp .= tempy
						Tempo.Push(outTemp)
						TempoDuration.Push(duration)
						IsNotNote.Push(" ")
						TempoIndex++
						prevDuration := duration
						pzero = 1
					}
				}
				; otherwise, add a note event
				if(subIndex = 6 && record = true && var > 0){
					postnotes := true
					var2 := Round(var, -1)
					Volume%ArrayIndex% := var2
					Durations%ArrayIndex% := duration
					Notes%ArrayIndex% := note
					TrackHolder%ArrayIndex% := track
					KeyState%ArrayIndex% := keyState
					ArrayIndex++
				}
			}
		}
	}
	global ClientData := ""
	; Transfer associative array values into Track-based arrays : Notes, Volumes, Rests
	Loop 25
	{
		Track%A_Index% := []
		Track%A_Index%Vol := []
		Track%A_Index%Duration := []
	}
	ArrayIndex += 1
	Loop %ArrayIndex%
	{
		tracker := TrackHolder%A_Index%
		note := Notes%A_Index%
		Track%tracker%.Push(note)
		dur := Durations%A_Index%
		Track%tracker%Duration.Push(dur)
		vol := Volume%A_Index%
		Track%tracker%Vol.Push(vol)
		if (trackcounter != tracker){
			trackcounter++
		}
	}
	
	; Distribute tracks using portmappper settings to all connected (5 potential) clients
	Loop, 5
	{
		TrackWriter(A_Index)
	}

	GoSub AddFiller

	Loop, 5
	{
		if (KeysToPlay%A_Index%.Length() != 0)
		{
			Index := A_Index
			OutArrays := CombineFlags(TimeToPlay%Index%, KeysToPlay%Index%, TempoDuration, Tempo, dupes = false)
			TimeToPlay%Index% := OutArrays.num.Clone()
			KeysToPlay%Index% := OutArrays.alpha.Clone()
			timeLength := TimeToPlay%Index%.Length()
			Loop %timeLength%
			{
				nextIndex := A_Index + 1
				OutputArray := TimeToPlay%Index%
				par1 := OutputArray[A_Index]
				par2 := OutputArray[nextIndex]
				par3 := par2 - par1
				par3 := par3 / 500000
				DelayToPlay%Index%.Push(par3)
			}
		}
	}
	
	Gosub, TrackSharer
return

TrackWriter(Index)
{
	OutNotes := []
	Loop, 8
	{
		Track%A_Index%Output := []
		Track%A_Index%DurationOutput := []
	}
	Loop, 8
	{
		Track%A_Index%Holder := []
		Track%A_Index%DurationHolder := []
	}

	; For each track 1–8:
    ;   1. Combine simultaneous notes with Combine(), merging them to together into a Rests array (TempDuration) and Notes array (Temp)
    ;   2. Add octave‑swap markers with AddSwaps()
    ;   3. Opportunistically insert octave changes with OpportunisticSwaps()
    ;   4. Map our note codes to Windows key‑codes via replaceChars()
	TrackIndex := 1
	Loop, 8
	{
		; Check if the track is selected in portmapper for the given IP address
		IpContents := Ip%Index%%A_Index%
		if (IpContents != 0)
		{
			OutNotes := [], Temp := [], TempDuration := []
			Track%TrackIndex%Holder := Track%A_Index%
			Track%TrackIndex%DurationHolder := Track%A_Index%Duration
			Tindex := Track%TrackIndex%Holder
			Tdur := Track%TrackIndex%DurationHolder
			
			OutNotes := Combine(Tdur, Tindex, TempDuration, Temp)
			Tdur := OutNotes.num.Clone()
			Tindex := OutNotes.alpha.Clone()

			Tindex := AddSwaps(Tindex)
			
			OutNotes := OpportunisticSwaps(Tindex, Tdur)
			Tdur := OutNotes.alpha.Clone()
			Tindex := OutNotes.num.Clone()
			
			replacements := []
			ascIn := 0
			if (TrackIndex == 1)
			{
				;192-207
				ascIn := 192
			}
			else if (TrackIndex == 2)
			{
				;208-223
				ascIn := 208
			}
			else if (TrackIndex == 3)
			{
				;224-239
				ascIn := 224
			}
			else if (TrackIndex == 4)
			{
				;240-255
				ascIn := 240
			}
			else if (TrackIndex == 5)
			{
				;256-271
				ascIn := 256
			}
			else if (TrackIndex == 6)
			{
				;272-287
				ascIn := 272
			}
			else if (TrackIndex == 7)
			{
				;288-303
				ascIn := 288
			}
			else if (TrackIndex == 8)
			{
				;304-319
				ascIn := 304
			}

			; Replace Track%Index% notes with unique set of Track-based ASCII characters
			replacements := [["z", Chr(ascIn)], ["Z", Chr(ascIn+1)], ["x", Chr(ascIn+2)], ["X", Chr(ascIn+3)], ["c", Chr(ascIn+4)]
			,["v", Chr(ascIn+5)], ["V", Chr(ascIn+6)], ["b", Chr(ascIn+7)], ["B", Chr(ascIn+8)], ["n", Chr(ascIn+9)], ["N", Chr(ascIn+10)]
			,["m", Chr(ascIn+11)], ["a", Chr(ascIn)], ["A", Chr(ascIn+1)], ["s", Chr(ascIn+2)], ["S", Chr(ascIn+3)], ["d", Chr(ascIn+4)]
			,["f", Chr(ascIn+5)], ["F", Chr(ascIn+6)], ["g", Chr(ascIn+7)], ["G", Chr(ascIn+8)], ["h", Chr(ascIn+9)], ["H", Chr(ascIn+10)]
			,["j", Chr(ascIn+11)], ["q", Chr(ascIn)], ["Q", Chr(ascIn+1)], ["w", Chr(ascIn+2)], ["W", Chr(ascIn+3)], ["e", Chr(ascIn+4)]
			,["r", Chr(ascIn+5)], ["R", Chr(ascIn+6)], ["t", Chr(ascIn+7)], ["T", Chr(ascIn+8)], ["y", Chr(ascIn+9)], ["Y", Chr(ascIn+10)]
			,["u", Chr(ascIn+11)], ["i", Chr(ascIn+12)], ["8", Chr(ascIn+12)], ["9", Chr(ascIn+13)], ["0", Chr(ascIn+14)]]
			Track%TrackIndex%Holder := replaceChars(Track%TrackIndex%Holder, replacements)

			Tindex := replaceChars(Tindex, replacements)
			Track%TrackIndex%DurationOutput := Tdur
			Track%TrackIndex%Output := Tindex
			if (Index != 1)
				TrackIndex++
		}
		if (Index == 1)
			TrackIndex++
	}

	;--------------- merge all tracks -----------------;
	OutNotes := Combine(Track1DurationOutput, Track1Output, Track2DurationOutput, Track2Output)
	Track1DurationOutput := OutNotes.num.Clone()
	Track1Output := OutNotes.alpha.Clone()

	OutNotes := Combine(Track1DurationOutput, Track1Output, Track3DurationOutput, Track3Output)
	Track1DurationOutput := OutNotes.num.Clone()
	Track1Output := OutNotes.alpha.Clone()

	OutNotes := Combine(Track1DurationOutput, Track1Output, Track4DurationOutput, Track4Output)
	Track1DurationOutput := OutNotes.num.Clone()
	Track1Output := OutNotes.alpha.Clone()

	OutNotes := Combine(Track1DurationOutput, Track1Output, Track5DurationOutput, Track5Output)
	Track1DurationOutput := OutNotes.num.Clone()
	Track1Output := OutNotes.alpha.Clone()

	OutNotes := Combine(Track1DurationOutput, Track1Output, Track6DurationOutput, Track6Output)
	Track1DurationOutput := OutNotes.num.Clone()
	Track1Output := OutNotes.alpha.Clone()

	OutNotes := Combine(Track1DurationOutput, Track1Output, Track7DurationOutput, Track7Output)
	Track1DurationOutput := OutNotes.num.Clone()
	Track1Output := OutNotes.alpha.Clone()

	OutNotes := Combine(Track1DurationOutput, Track1Output, Track8DurationOutput, Track8Output)
	TimeToPlay%Index% := OutNotes.num.Clone()
	KeysToPlay%Index% := OutNotes.alpha.Clone()
	;--------------- merge all tracks -----------------;
}

; Merges notes occurring on the same beat, adds octave swaps, and replaces the notes with keys mapped for output to specific windows. 
TrackSharer:
	global SocketIDs
	global T1
	global SongIndex1 := 1
	global SongIndex2 := 1
	global SongIndex3 := 1
	global SongIndex4 := 1
	global SongIndex5 := 1
	global startTime1 := 0
	global startTime2 := 0
	global startTime3 := 0
	global startTime4 := 0
	global startTime5 := 0
	tempotemp := 1
	; Compare latencies between servers and clients to determine the amount of delay to impose
	; Latency is determined using the difference between the highest socket ping and each players individual socket ping
	; Computers with higher input delay will start their song slightly before the server
	Loop, 5
	{
		if (KeysToPlay%A_Index%.Length() > 3)
		{
			ServerPing := Pings[1]
			AddPing := Ip%A_Index%GW2
			latency%A_Index% := AddPing - ServerPing
		}
	}
	
	Loop, 5
	{
		; Check if client has a track to play
		if (KeysToPlay%A_Index%.Length() > 3)
		{
			if (A_Index == 1)
				startMSec := latency%A_Index%
			Else
				startMSec := latency%A_Index%
			DelayToClock(A_Index)
		}
	}
	T1 := A_TickCount
	; Get A_TickCount from each client
	Loop, 5
	{
		if (SocketIDs[A_Index] != 0 && SocketIDs[A_Index] != 1)
		{
			sText := "Tick_Count"
			SendTextToClient(sText, SocketIDs[A_Index])
			;Sleep, 1
		}
	}
	
	TickCounts[1] := A_TickCount

	; Wait for Tickcounts to be received
	Sleep, 2000

	; Queue all note events based on the client's A_TickCount, factoring in each client's individuals GW2 Ping
	Loop, 5
	{
		TimeToPlay%A_Index% := []
		prevIndex := 0
		Index := A_Index
		if (NewNotes%A_Index%.Length() > 3)
		{
			for i, v in DelayToPlay%A_Index%
			{
				if (i == 1)
				{
					; 4000 wait duration - 2000 sleep duration = 2 seconds until the song starts
					TimeToPlay%Index%[i] := DelayToPlay%Index%[i] + 4000 - latency%Index% + TickCounts[Index]
				}
				Else
				{
					TimeToPlay%Index%[i] := prevIndex + DelayToPlay%Index%[i]
				}
				prevIndex := TimeToPlay%Index%[i]
			}
		}
	}
	; Send song to clients
	Loop, 5
	{
		Index := A_Index
		if ((NewNotes%A_Index%.Length() > 3) && (A_Index != 1))
		{
			SongIndex2 := 1
			RestGroup := "Rest_1"
			NoteGroup := "Note_1"
			len := NewNotes%A_Index%.Length()
			Loop, % len
			{
				note := NewNotes%Index%[SongIndex2]
				rest := TimeToPlay%Index%[SongIndex2]
				SongIndex2++
				RestGroup .= "," . rest
				NoteGroup .= "," . note
			}
			SendTextToClient(RestGroup, SocketIDs[A_Index])
        	SendTextToClient(NoteGroup, SocketIDs[A_Index])
		}
	}
	startTime1 := TimeToPlay1[1]
	SetTimer, SongPlayer1, -1
	;startTime2 := TimeToPlay2[len] - 4000
	;SetTimer, SongPlayer2, -1
return

; Convert DelayToPlay into a system clock time schedule
DelayToClock(Index)
{
	ClockTimes%Index% := [], MSecTimes%Index% := [], NewNotes%Index% := []

	; Add notes and msec delays between notes
	songLength := DelayToPlay%Index%.Length()
	Loop %songLength%
	{
		kindex := KeysToPlay%Index%
		dindex := DelayToPlay%Index%
		item := kindex[A_Index]
		first := SubStr(item, 1, 1)
		if (first == "B"){
			tempotemp := SubStr(item, 2)
			sleepDuration := dindex[A_Index] * tempotemp * DefaultTempo
			newMSec := sleepDuration
			newMSec := Floor(newMSec)
			NewNotes%Index%.Push("")
			MSecTimes%Index%.Push(newMSec)
		}
		else
		{
			sleepDuration := dindex[A_Index] * tempotemp * DefaultTempo
			newMSec := sleepDuration
			newMSec := Floor(newMSec)
			NewNotes%Index%.Push(kindex[A_Index])
			MSecTimes%Index%.Push(newMSec)
		}
	}

	DelayToPlay%Index% := []

	Loop %songLength%
	{
		mindex := MSecTimes%Index%
		mitem := mindex[A_Index]
		DelayToPlay%Index%.Push(Round(mindex[A_Index]))
	}
}

; Server song-playing loop
SongPlayer1:
	global Stop
	SetTimer, SongPlayer1, Off
	global SongIndex1
	SongLength1 := NewNotes1.Length()
	if (Stop = true)
	{
		SetTimer, SongPlayer1, Off
		return
	}
	rest := TimetoPlay1[SongIndex1]
	if (A_TickCount >= rest)
	{
		nextKey := NewNotes1[SongIndex1+1]
		PlayNotes(nextKey)
		SongIndex1++
	}

	if (SongIndex1 <= SongLength1)
	{
		; keep same logic you had for next start time
		SetTimer, SongPlayer1, 1
	}
	else
	{
		; reached end of song — timer stays off
		; (optionally set Stop := true here if you want)
		SetTimer, SongPlayer1, Off
		Return
	}
Return

; Request A_TickCount from each connected client
SongPlayer2:
	global Stop
	global T1
	global startTime2
    if (Stop)
    {
		SetTimer, SongPlayer2, Off   ; stop future timer calls
        return
    }
    if (A_TickCount >= startTime2)
	{
		; Get A_TickCount from each client
		Loop, 5
		{
			if (SocketIDs[A_Index] != 0 && SocketIDs[A_Index] !=1)
			{
				sText := "Tick_Count"
				SendTextToClient(sText, SocketIDs[A_Index])
			}
		}
		T1 := A_TickCount
		TickCounts[1] := A_TickCount
		;SetTimer, Songplayer3, -1
		SetTimer, SongPlayer2, Off   ; stop future timer calls
        Return                      ; exit immediately 
    }
	SetTimer, SongPlayer2, 1
    ; if you actually want the timer to continue when not started, keep or adjust as needed
Return

SongPlayer3:
	global Stop
	global DelayToPlayHalf1, global DelayToPlayHalf2, global DelayToPlayHalf3, global DelayToPlayHalf4, global DelayToPlayHalf5
	global NewNotesHalf1, global NewNotesHalf2, global NewNotesHalf3, global NewNotesHalf4, global NewNotesHalf5
	global latency1, global latency2, global latency3, global latency4, global latency5
	global TickCounts

	if (Stop)
    {
		SetTimer, SongPlayer3, Off   ; stop future timer calls
        return
    }

	if (A_TickCount >= startTime3)
	{
		global TimeToPlayHalf1 := [], global TimeToPlayHalf2 := [], global TimeToPlayHalf3 := [], global TimeToPlayHalf4 := [], global TimeToPlayHalf5 := []
		; Queue all note events based on the client's A_TickCount, factoring in each client's individuals GW2 Ping
		Loop, 5
		{
			if (A_Index != 1)
			{
				TimeToPlayHalf%A_Index% := []
				prevIndex := 0
				Index := A_Index
				if (NewNotesHalf%A_Index%.Length() > 3)
				{
					for i, v in DelayToPlayHalf%A_Index%
					{
						if (i == 1)
						{
							; 4000 wait duration - 2000 sleep duration = 2 seconds until the song starts
							test := latency%Index%
							TimeToPlayHalf%Index%[i] := DelayToPlayHalf%Index%[i] + 4000 - latency%Index% + TickCounts[Index]
						}
						Else
						{
							TimeToPlayHalf%Index%[i] := prevIndex + DelayToPlayHalf%Index%[i]
						}
						prevIndex := TimeToPlayHalf%Index%[i]
					}
				}
			}
		}

		; Send song to clients
		Loop, 5
		{
			Index := A_Index
			if ((NewNotesHalf%A_Index%.Length() > 3) && (A_Index != 1))
			{
				SongIndex2 := 1
				RestGroup := "Rest_2"
				NoteGroup := "Note_2"
				len := NewNotesHalf%A_Index%.Length()
				Loop, % len
				{
					note := NewNotesHalf%Index%[SongIndex2]
					rest := TimeToPlayHalf%Index%[SongIndex2]
					SongIndex2++
					RestGroup .= "," . rest
					NoteGroup .= "," . note
				}
				SendTextToClient(RestGroup, SocketIDs[A_Index])
				SendTextToClient(NoteGroup, SocketIDs[A_Index])
			}
		}

        SetTimer, SongPlayer3, Off   ; stop future timer calls
        return                      ; exit immediately
	}
	SetTimer, SongPlayer3, 1
Return

SongPlayerHalf3:
	; Send second half of song
	global latencyHalf3
	if (Stop)
    {
		SetTimer, SongPlayerHalf3, Off   ; stop future timer calls
        return
    }

	if (A_TickCount >= latencyHalf3)
	{
		SongIndex3 := 1
		RestGroup3 := "Rest"
        NoteGroup3 := "Note"
        len := NewNotesHalf3.Length()
        Loop, % len
        {
            note3 := NewNotesHalf3[SongIndex3]
            rest3 := DelayToPlayHalf3[SongIndex3]

            if (note3 != "")
            {
                SongIndex3++
                RestGroup3 .= "," . rest3
                NoteGroup3 .= "," . note3
            }
            else
            {
                SongIndex3++
                if (rest3 != 0)
                {
                    RestGroup3 .= "," . rest3
                    NoteGroup3 .= ",F"
                }
            }
		}
		 ; send and then stop timer + exit
        SendTextToClient(RestGroup3, SocketIDs[3])
        SendTextToClient(NoteGroup3, SocketIDs[3])

        SetTimer, SongPlayerHalf3, Off   ; stop future timer calls
        return                      ; exit immediately
	}
	SetTimer, SongPlayerHalf3, 1
Return

SongPlayer4:
    SetTimer, SongPlayer4, Off
	SongLength := NewNotes4.Length()
    if (Stop = true)
    {
        return
    }

    ;---- time check (unchanged) ----
    curTime := A_Now
    total := Mod(A_Msec, 1000)
    msecStr := SubStr("000" . total, -4)
    curTime .= msecStr

    if (curTime >= startTime4)
        start4 := true
    else
        start4 := false

    if (start4 == true)
    {
        pairsSent := 0
		PacketGroup := "PacketGroup"
		; If packet groups are sent to frequently, the players will become out of sync
		; as a result, we need a random chance for the packet group to be skipped and use a Send command instead
		Random, rand, 1, 4
		if (rand != 1)
		{
			Loop, 5
			{
				note := NewNotes4[SongIndex4]
				rest := DelayToPlay4[SongIndex4]
				; The goal is to opportunistically build groups of send events instead of sending rapid send events
				; This helps keep notes played with consistent tempo even when played in quick succession
				if ((SongIndex4 > SongLength) || (rest > 500))
					break
				
				if (note != "")
				{
					SongIndex4++
					pairsSent++
					PacketGroup .= "," . note . "," . rest
				}
				Else
				{
					SongIndex4++
					pairsSent++
					PacketGroup .= "," . "F" . "," . rest
				}
				; if next note is a filler note, stop filling the packet and send it
				; we don't want packets with lengthy sleep durations which could risk desync
				; instead, we'll check the next index to see if it's a long rest and send the packet if it is
				if (DelayToPlay4[SongIndex4] > 500)
					break
			}
		}
		if ((NewNotes4[SongIndex4] != "") && (pairsSent = 0))
		{
			note := NewNotes4[SongIndex4]
			sText := "Send,"
			sText .= note
			SendTextToClient(sText, SocketIDs[4])
			SongIndex4++
		}
		if ((NewNotes4[SongIndex4] = "") && (pairsSent = 0))
			SongIndex4++
		
        ; send the packet group if we collected anything
        if (pairsSent > 0)
            SendTextToClient(PacketGroup, SocketIDs[4])

        ; set next trigger time, or stop when the song is finished
        if (SongIndex4 <= SongLength)
        {
            ; keep same logic you had for next start time
            startTime4 := ClockTimes4[SongIndex4-1]
            SetTimer, SongPlayer4, 1
        }
        else
        {
            ; reached end of song — timer stays off
            ; (optionally set Stop := true here if you want)
            Return
        }
    }
	SetTimer, SongPlayer4, 1
Return

SongPlayer5:
    SetTimer, SongPlayer5, Off
	SongLength := NewNotes5.Length()
    if (Stop = true)
    {
        return
    }
    curTime := A_Now
    total := Mod(A_Msec, 1000)
    msecStr := SubStr("000" . total, -3)
    curTime .= msecStr

    if (curTime >= startTime5)
        start5 := true
    else
        start5 := false

    if (start5 == true)
    {
        pairsSent := 0
		PacketGroup := "PacketGroup"
		Loop, 5
		{
			note := NewNotes5[SongIndex5]
			rest := DelayToPlay5[SongIndex5]
			if ((SongIndex5 > SongLength) || (rest > 1000) || (note = "F") || (note = ""))
				break	
			SongIndex5++
			pairsSent++
			PacketGroup .= "," . note . "," . rest
			if ((NewNotes5[SongIndex5] = "F") || (DelayToPlay5[SongIndex5] > 1000))
				break
		}
		if ((NewNotes5[SongIndex5] != "F") && (NewNotes5[SongIndex5] != ""))
		{
			note := NewNotes5[SongIndex5]
			sText := "Send,"
			sText .= note
			SendTextToClient(sText, SocketIDs[5])
			SongIndex5++
		}
		Else
			SongIndex5++
        if (pairsSent > 0)
            SendTextToClient(PacketGroup, SocketIDs[5])
        if (SongIndex5 <= SongLength)
        {
            startTime5 := ClockTimes5[SongIndex5-1]
            SetTimer, SongPlayer5, 1
        }
        else
        {
            Return
        }
    }
	SetTimer, SongPlayer5, 1
Return

OpportunisticSwaps(a1, a2)
{
    a3 := [], a4 := []
    len := a1.Length()
    Loop %len%
        {
            if (A_Index != 1)
                {
                    if (InStr(a1[A_Index], "9", false, 1, 1) == 1)
                        {
                            dif := a2[A_Index] - a2[A_Index-1] 
                            if (dif >= Delay)
                                {
                                    a3.Push("9")
                                    addSleep := Floor(a2[A_Index-1] + (dif/2))
                                    a4.Push(addSleep)
                                    ReplacedStr := StrReplace(a1[A_Index], "9",,,1)
                                    a3.Push(ReplacedStr)
                                    a4.Push(a2[A_Index])
                                }
                            Else
                                {
                                    a3.Push(a1[A_Index])
                                    a4.Push(a2[A_Index])
                                }
                        }
                    else if (InStr(a1[A_Index], "0", false, 1, 1) == 1)
                        {
                            dif := a2[A_Index] - a2[A_Index-1] 
                            if (dif >= Delay)
                                {
                                    a3.Push("0")
                                    addSleep := Floor(a2[A_Index-1] + (dif/2))
                                    a4.Push(addSleep)
                                    ReplacedStr := StrReplace(a1[A_Index], "0",,,1)
                                    a3.Push(ReplacedStr)
                                    a4.Push(a2[A_Index])
                                }
                            Else
                                {
                                    a3.Push(a1[A_Index])
                                    a4.Push(a2[A_Index])
                                }
                        }
                    Else
                        {
                            a3.Push(a1[A_Index])
                            a4.Push(a2[A_Index])
                        }
                }
                Else
                    {
                        a3.Push(a1[A_Index])
                        a4.Push(a2[A_Index])
                    }
        }
        return {num: a3, alpha: a4}
}

AddSwaps(a1)
{
	; Purpose: Processes a list of keypress strings (`a1`) and modifies them based on octave changes,
	;          depending on which keys are involved.

	len := a1.Length()
	octave = 1
	; Key sets are used for octave-based comparison, allowing the script to determine which octave it started in,
	; which it is going to, and whether or not a "C" note should be played as a 1 or an 8.
	; Set 1 is for middle octave, set 2 is for low octave, and set 3 is for high octave
	set1 := "aAsSdfFgGhHjqQwWerRtTyYuizZxXcvVbBnNm90"
	set2 := "zZxXcvVbBnNmaAsSdfFgGhHjkq"
	set3 := "qQwWerRtTyYuiaAsSdfFgGhHj"

	; Output array
	a3 := []

	Loop %len% {
		notes := a1[A_Index]
		newStr := ""

		; Processing for Octave 1
		if (octave = 1)
		{
			Loop, parse, set1
			{
				i := A_LoopField
				if notes contains %i%
				{
					; Handles pesky "C" notes
					if i contains a
						{
							; Case : we have low octave keys but no middle octave keys
							if notes contains z,Z,x,X,c,v,V,b,B,n,N,m  ; If notes contain lower row keys
								{
									if notes not contains A,s,S,d,f,F,g,G,h,H,j  ; But not middle row
										{
											if (octave == 1)
												{
													octave -= 1      ; Go down one octave
													newStr .= "9"    ; Append 9 to down-swap octaves
												}
												newStr .= "8"        ; Since we are swapping octaves down, 1's need to be played as 8's
										}
										Else
											{
												newStr .= i
											}
								}
							Else
								{
									newStr .= i
								}
						}
					; Case : we have high octave keys
					else if i contains Q,w,W,e,r,R,t,T,y,Y,u,i
					{
						if (octave = 1)
						{
							octave += 1
							newStr .= "0"
						}
						newStr .= i
					}
					; Case : we have low octave keys
					else if i contains z,Z,x,X,c,v,V,b,B,n,N,m
					{
						if (octave = 1)
						{
							octave -= 1
							newStr .= "9"
						}
						newStr .= i
					}
					; Case : we have high octave c' note
					else if i contains q
					{
						if notes contains Q,w,W,e,r,R,t,T,y,Y,u,i
							{
								if (octave = 1)
									{
										octave += 1
										newStr .= "0"
									}
									newStr .= i
							}
						Else
							{
								newStr .= "8"
							}
					}
					else
					{
						newStr .= i
					}
				}
			}
		}
		; Processing for Octave 0 (lower octave), similar logic applies here as compared to above
		else if (octave = 0)
		{
			Loop, parse, set2
			{
				i := A_LoopField
				if notes contains %i%
				{
					if i contains a
						{
							if notes contains A,s,S,d,f,F,g,G,h,H,j
								{
									if (octave = 0)
										{
											octave += 1
											newStr .= "0"
										}
										newStr .= i
								}
							Else
								{
									newStr .= "8"
								}
						}
					else if i contains q
						{
							if (octave == 0)
								{
									octave += 1
									newStr .= "0"
								}
								newStr .= "8"
						}
					else if i contains A,s,S,d,f,F,g,G,h,H,j,k
					{
						if (octave == 0)
						{
							octave += 1
							newStr .= "0"
						}
						newStr .= i
					}
					else
					{
						newStr .= i
					}
				}
			}
		}
		else if (octave = 2)
		{
			Loop, parse, set3
			{
				i := A_LoopField
				if notes contains %i%
				{
					if i contains q
						{
							if notes contains a,A,s,S,d,f,F,g,G,h,H,j
								{
									if notes not contains Q,w,W,e,r,R,t,T,y,Y,u,i
										{
											if (octave = 2)
												{
													octave -= 1
													newStr .= "9"
												}
												newStr .= "8"
										}
										Else
											{
												newStr .= i
											}
								}
							Else
								{
									newStr .= i
								}
						}
					else if i contains a,A,s,S,d,f,F,g,G,h,H,j
					{
						if (octave == 2)
						{
							octave -= 1
							newStr .= "9"
						}
						newStr .= i
					}
					else
					{
						newStr .= i
					}
				}
			}
		}
		a3.Push(newStr)
	}
	return a3
}

replaceChars(a1, a2)
{
	len := a1.Length()
	a3 := []
	Loop %len%
	{
		var := a1[A_Index]

		For inputVar, outputVar in a2
		{
			var := StrReplace(var, outputVar.1, outputVar.2)
		}
		a3.Push(var)
	}
	return a3
}

Combine(a1, a2, a3, a4) {
	a25 := [], a26 := [], t := []
	for k,v in a1
		if(v != "")
			list .= v "|"
	for k,v in a3
		if(v != "")
			list .= v "|"
	list := LTrim(list, "|")
	Sort, list, UND|
	a25 := StrSplit(list, "|")
	for k,v in a1
		t[a1[k]] := a2[k] . t[v]
	for k,v in a3
		t[a3[k]] := a4[k] . t[v]
	for k,v in t ; transfer associative array to simple array
		a26.Push(v)
	return {num: a25, alpha: a26}
}

CombineFlags(a1, a2, a3, a4, dupes) {
	a5 := [], a6 := [], t := []
	for k,v in a1
		if(v != "")
			list .= v "|"
	for k,v in a3
		if(v != "")
			list .= v "|"
	list := LTrim(list, "|")
	if (!dupes){
		Sort, list, ND|
	}else{
		Sort, list, UND|
	}
	a5 := StrSplit(list, "|")
	for k,v in a1
		t[a1[k]] := a2[k] . t[v]

	adjust = 0
	for k,v in a3{
		checknull := t[v]
		x := v + adjust
		if (x =){
			x := x - 1
		}
		if(checknull =){
			if(a4[k] != ""){
				t[x] := a4[k]
			}
		}else{
			if(a4[k] != ""){
				t.Insert(x - 1, a4[k])
				adjust++
			}
		}
	}
	for k,v in t
		a6.Push(v)
	return {num: a5, alpha: a6}
}

; Uses Dispatch keyset to convert ASCII characters into a corresponding number key (1234567890) and guild wars 2 window (Player1, Player2, Player3, Player4, Player5)
PlayNotes(notes)
{
    global Dispatch
    Loop, parse, notes
	{
		rec := Dispatch[A_LoopField]
        if !rec
            continue

        recwin := rec.win

        ; Resolve window safely
        hwnd := WinExist(recwin)
        if (!hwnd)
            continue

        key := rec.key

        ; Convert key name -> virtual key
        vk := GetKeyVK(key)
        if (!vk)
            continue

        ; Build lParam (repeat=1, scan code)
        sc := GetKeySC(key)
        lParamDown := 1 | (sc << 16)
        lParamUp   := 1 | (sc << 16) | (1 << 30) | (1 << 31)

        ; WM_KEYDOWN = 0x100, WM_KEYUP = 0x101
        PostMessage, 0x100, vk, lParamDown,, ahk_id %hwnd%
        PostMessage, 0x101, vk, lParamUp,,   ahk_id %hwnd%
	}
}

; Creates an array of filler notes which guarantee songplayer arrays are uniform lengths
ReplaceWithFiller(a1)
{
	len := a1.Length()
	a2 := []
	Loop %len%
	{
		a2.Push("")
	}
	return a2
}

; Adds Filler notes and delays to guarantee each client has a sufficiently similar length song
; Without filler notes, staggered tracks may play at different times or fall out of sync
AddFiller:
	OutNotes := []
	DelayFiller := []
	TrackFiller := []

	if (KeysToPlay1.Length() > 3)
	{
	Filler := Combine(DelayFiller, TrackFiller, TimeToPlay1, KeysToPlay1)
	DelayFiller := Filler.num.Clone()
	TrackFiller := Filler.alpha.Clone()
	}
	if (KeysToPlay2.Length() > 3)
	{
	Filler := Combine(DelayFiller, TrackFiller, TimeToPlay2, KeysToPlay2)
	DelayFiller := Filler.num.Clone()
	TrackFiller := Filler.alpha.Clone()
	}
	if (KeysToPlay3.Length() > 3)
	{
	Filler := Combine(DelayFiller, TrackFiller, TimeToPlay3, KeysToPlay3)
	DelayFiller := Filler.num.Clone()
	TrackFiller := Filler.alpha.Clone()
	}
	if (KeysToPlay4.Length() > 3)
	{
	Filler := Combine(DelayFiller, TrackFiller, TimeToPlay4, KeysToPlay4)
	DelayFiller := Filler.num.Clone()
	TrackFiller := Filler.alpha.Clone()
	}
	if (KeysToPlay5.Length() > 3)
	{
	Filler := Combine(DelayFiller, TrackFiller, TimeToPlay5, KeysToPlay5)
	DelayFiller := Filler.num.Clone()
	TrackFiller := Filler.alpha.Clone()
	}
	if (KeysToPlay6.Length() > 3)
	{
	Filler := Combine(DelayFiller, TrackFiller, TimeToPlay6, KeysToPlay6)
	DelayFiller := Filler.num.Clone()
	TrackFiller := Filler.alpha.Clone()
	}
	if (KeysToPlay7.Length() > 3)
	{
	Filler := Combine(DelayFiller, TrackFiller, TimeToPlay7, KeysToPlay7)
	DelayFiller := Filler.num.Clone()
	TrackFiller := Filler.alpha.Clone()
	}
	if (KeysToPlay8.Length() > 3)
	{
	Filler := Combine(DelayFiller, TrackFiller, TimeToPlay8, KeysToPlay8)
	DelayFiller := Filler.num.Clone()
	TrackFiller := Filler.alpha.Clone()
	}

	TrackFiller := ReplaceWithFiller(TrackFiller)
	
	Filler := Combine(TimeToPlay1, KeysToPlay1, DelayFiller, TrackFiller)
	TimeToPlay1 := Filler.num.Clone()
	KeysToPlay1 := Filler.alpha.Clone()
	if (KeysToPlay2.Length() > 3)
	{
	Filler := Combine(TimeToPlay2, KeysToPlay2, DelayFiller, TrackFiller)
	TimeToPlay2 := Filler.num.Clone()
	KeysToPlay2 := Filler.alpha.Clone()
	}
	if (KeysToPlay3.Length() > 3)
	{
	Filler := Combine(TimeToPlay3, KeysToPlay3, DelayFiller, TrackFiller)
	TimeToPlay3 := Filler.num.Clone()
	KeysToPlay3 := Filler.alpha.Clone()
	}
	if (KeysToPlay4.Length() > 3)
	{
	Filler := Combine(TimeToPlay4, KeysToPlay4, DelayFiller, TrackFiller)
	TimeToPlay4 := Filler.num.Clone()
	KeysToPlay4 := Filler.alpha.Clone()
	}
	if (KeysToPlay5.Length() > 3)
	{
	Filler := Combine(TimeToPlay5, KeysToPlay5, DelayFiller, TrackFiller)
	TimeToPlay5 := Filler.num.Clone()
	KeysToPlay5 := Filler.alpha.Clone()
	}
	if (KeysToPlay6.Length() > 3)
	{
	Filler := Combine(TimeToPlay6, KeysToPlay6, DelayFiller, TrackFiller)
	TimeToPlay6 := Filler.num.Clone()
	KeysToPlay6 := Filler.alpha.Clone()
	}
	if (KeysToPlay7.Length() > 3)
	{
	Filler := Combine(TimeToPlay7, KeysToPlay7, DelayFiller, TrackFiller)
	TimeToPlay7 := Filler.num.Clone()
	KeysToPlay7 := Filler.alpha.Clone()
	}
	if (KeysToPlay8.Length() > 3)
	{
	Filler := Combine(TimeToPlay8, KeysToPlay8, DelayFiller, TrackFiller)
	TimeToPlay8 := Filler.num.Clone()
	KeysToPlay8 := Filler.alpha.Clone()
	}
Return