; Paste sheet music, press F1, then play the notes yourself. The script
; switches octaves for you (9 = down, 0 = up), only when the next note of
; the sheet is in another octave, and only once you have played the note
; before it: the right number, in the right octave. A wrong note still
; plays, but the script keeps waiting for the right one.
;
; Notes joined by / are a chord, like [3/5/8]: play them in any order. The
; octave switches once every note of the chord has been played.
;
; Press the numbers the sheet shows, on the row of the note's octave:
;   a s d f g h j k  = low octave
;   q w e r t y u i  = middle octave
;   1 2 3 4 5 6 7 8  = high octave
;   numpad 1-8       = any octave
; To use any row for any octave, set RowOctaves to false below.
;
; [ ] or a down triangle = low octave, ( ) or an up triangle = high octave.
;
; The game must start in the middle octave.

#NoEnv
#SingleInstance Force
#KeyHistory 0
#MaxHotkeysPerInterval 99000000
#HotkeyInterval 99000000
#MaxThreadsBuffer On
ListLines Off
Process, Priority, , High
SetBatchLines, -1
SetKeyDelay, -1
SetMouseDelay, -1
SetDefaultMouseSpeed, 0
SetWinDelay, -1
SetControlDelay, -1

;~~~~~~~~~Try Out Different Delays~~~~~~~~~
; Minimum time in ms between two octave key presses
global Delay := 110
;~~~~~~~~~Try Out Different Delays~~~~~~~~~

; true: a note only counts when it is pressed on the row of its octave
; false: a note counts on any row
global RowOctaves := true

global Song := []          ; the sheet in order, one chord (or single note) each: {notes, octave}
global Pos := 1            ; index of the chord being played
global GameOctave := 2     ; octave the game is in: 1 = low, 2 = middle, 3 = high
global Loaded := false
global Playing := false
global HeldKeys := {}      ; keys held down, so keyboard auto-repeat is ignored
global LastOctave := 0     ; time (ms) of the last octave key press

; Hotkeys that only exist while a song is playing, so the keys type
; normally when it is not
global PlayHotkeys := StrSplit("$a $s $d $f $g $h $j $k $q $w $e $r $t $y $u $i $1 $2 $3 $4 $5 $6 $7 $8 $9 $0", " ")
SetPlayHotkeys("Off")

; 1 ms timer resolution so the octave delay is accurate
DllCall("Winmm\timeBeginPeriod", "UInt", 1)
OnExit("Cleanup")

Gui, Add, Text,, Paste Sheet Music Below
Gui, Add, Edit, R10 w400 vPianoCode
Gui, Add, Text,, Copy/paste sheet music into the box, press F1 to play/pause a song
Gui, Add, Text,, F2 to reload the application
Gui, Add, Text,, F3 to close the application
Gui, Add, Text,, Version 0.28
Gui, Show
return

F1::
	Critical
	HeldKeys := {}
	if (!Loaded) {
		Gui, Submit, NoHide
		Song := ParseSheet(PianoCode)
		Loaded := true
		Playing := true
		SetPlayHotkeys("On")
		Pos := 1
		ToNextOctave() ; the first note may be in another octave
	} else {
		Playing := !Playing
		SetPlayHotkeys(Playing ? "On" : "Off")
	}
return

;~~~~~~~~~Low octave~~~~~~~~~
$a::NoteDown(1, 1)
$s::NoteDown(2, 1)
$d::NoteDown(3, 1)
$f::NoteDown(4, 1)
$g::NoteDown(5, 1)
$h::NoteDown(6, 1)
$j::NoteDown(7, 1)
$k::NoteDown(8, 1)

;~~~~~~~~~Middle octave~~~~~~~~~
$q::NoteDown(1, 2)
$w::NoteDown(2, 2)
$e::NoteDown(3, 2)
$r::NoteDown(4, 2)
$t::NoteDown(5, 2)
$y::NoteDown(6, 2)
$u::NoteDown(7, 2)
$i::NoteDown(8, 2)

;~~~~~~~~~High octave~~~~~~~~~
$1::NoteDown(1, 3)
$2::NoteDown(2, 3)
$3::NoteDown(3, 3)
$4::NoteDown(4, 3)
$5::NoteDown(5, 3)
$6::NoteDown(6, 3)
$7::NoteDown(7, 3)
$8::NoteDown(8, 3)

;~~~~~~~~~Any octave~~~~~~~~~
$Numpad1::NoteDown(1, 0)
$Numpad2::NoteDown(2, 0)
$Numpad3::NoteDown(3, 0)
$Numpad4::NoteDown(4, 0)
$Numpad5::NoteDown(5, 0)
$Numpad6::NoteDown(6, 0)
$Numpad7::NoteDown(7, 0)
$Numpad8::NoteDown(8, 0)

;~~~~~~~~~Manual octave keys~~~~~~~~~
$9::OctaveKeyDown(9)
$0::OctaveKeyDown(0)
$Numpad0::OctaveKeyDown(9)
$Numpad9::OctaveKeyDown(0)

$a Up::
$s Up::
$d Up::
$f Up::
$g Up::
$h Up::
$j Up::
$k Up::
$q Up::
$w Up::
$e Up::
$r Up::
$t Up::
$y Up::
$u Up::
$i Up::
$1 Up::
$2 Up::
$3 Up::
$4 Up::
$5 Up::
$6 Up::
$7 Up::
$8 Up::
$9 Up::
$0 Up::
$Numpad1 Up::
$Numpad2 Up::
$Numpad3 Up::
$Numpad4 Up::
$Numpad5 Up::
$Numpad6 Up::
$Numpad7 Up::
$Numpad8 Up::
$Numpad9 Up::
$Numpad0 Up::
	KeyUp()
return

; Turns sheet music into the list of chords to play, in order. A chord holds
; the notes joined by / (any order), or a single note, and its octave.
ParseSheet(text) {
	downMarks := Chr(0x25BC) . ")[" ; down triangle, ) and [
	upMarks := Chr(0x25B2) . "(]"   ; up triangle, ( and ]
	chords := []
	octave := 2   ; the sheet starts in the middle octave
	join := false ; a / came after the last note
	Loop, Parse, text
	{
		c := A_LoopField
		if InStr(downMarks, c) {
			if (octave > 1)
				octave--
		} else if InStr(upMarks, c) {
			if (octave < 3)
				octave++
		} else if (c = "/") {
			join := true
		} else if InStr("12345678", c) {
			; A note after a / joins the chord before it, if both are in the
			; same octave (a chord can't be played in any order across octaves)
			n := chords.Length()
			if (join && n && chords[n].octave = octave)
				chords[n].notes .= c
			else
				chords.Push({notes: c, octave: octave})
			join := false
		}
	}
	return chords
}

; octave is the octave of the key's row: 1 = low, 2 = middle, 3 = high, 0 = any
NoteDown(digit, octave) {
	; Notes pressed while an octave switch is waiting are queued until it is
	; done, so they play in the right octave and are counted in order
	Critical
	key := SubStr(A_ThisHotkey, 2)
	if HeldKeys.HasKey(key)
		return
	HeldKeys[key] := true
	SendInput, {%digit%}
	if (!Playing || Pos > Song.Length())
		return
	; Only a note of the current chord counts, and only in its own octave
	chord := Song[Pos]
	if (RowOctaves && octave && octave != chord.octave)
		return
	if !(i := InStr(chord.notes, digit))
		return
	chord.notes := SubStr(chord.notes, 1, i - 1) . SubStr(chord.notes, i + 1)
	if (chord.notes != "")
		return ; notes of the chord still to play
	Pos++
	ToNextOctave()
}

OctaveKeyDown(digit) {
	Critical
	key := SubStr(A_ThisHotkey, 2)
	if HeldKeys.HasKey(key)
		return
	HeldKeys[key] := true
	SendInput, {%digit%}
	LastOctave := NowMs()
}

KeyUp() {
	key := SubStr(A_ThisHotkey, 2, -3) ; "$a Up" -> "a"
	; A key pressed before the hotkeys were turned on still gets released
	if !HeldKeys.HasKey(key)
		SendInput, {Blind}{%key% Up}
	HeldKeys.Delete(key)
}

; Switches to the octave of the next note, if the game is not in it already
ToNextOctave() {
	if (Pos > Song.Length())
		return
	octave := Song[Pos].octave
	while (GameOctave > octave) {
		PressOctave("9")
		GameOctave--
	}
	while (GameOctave < octave) {
		PressOctave("0")
		GameOctave++
	}
}

PressOctave(digit) {
	wait := Delay - (NowMs() - LastOctave)
	if (wait > 0)
		DllCall("Sleep", "UInt", Ceil(wait))
	SendInput, {%digit%}
	LastOctave := NowMs()
}

NowMs() {
	static freq := 0
	if (!freq)
		DllCall("QueryPerformanceFrequency", "Int64*", freq)
	DllCall("QueryPerformanceCounter", "Int64*", now)
	return now * 1000 / freq
}

SetPlayHotkeys(state) {
	for _, hk in PlayHotkeys {
		Hotkey, %hk%, %state%
		Hotkey, %hk% Up, %state%
	}
}

Cleanup(reason := "", code := "") {
	DllCall("Winmm\timeEndPeriod", "UInt", 1)
}

;Reload & Exit Keybinds;
F2::Reload
F3::ExitApp
GuiClose:
ExitApp
