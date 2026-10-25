; Paste sheet music, press F1, then play the notes yourself. The script
; switches octaves for you (9 = down, 0 = up) as soon as every note of the
; current octave section has been played.
;
; Press the numbers the sheet shows, on any of these rows:
;   a s d f g h j k  /  q w e r t y u i  /  1 2 3 4 5 6 7 8  /  numpad 1-8
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

global Song := []          ; note groups (notes 1-8 to play, in any order) and "9"/"0" octave steps
global Pos := 1            ; index of the group being played
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
Gui, Add, Text,, Version 0.26
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
		Pos := 0
		NextGroup() ; press the octave keys the first note needs
	} else {
		Playing := !Playing
		SetPlayHotkeys(Playing ? "On" : "Off")
	}
return

;~~~~~~~~~Low octave~~~~~~~~~
$a::NoteDown(1)
$s::NoteDown(2)
$d::NoteDown(3)
$f::NoteDown(4)
$g::NoteDown(5)
$h::NoteDown(6)
$j::NoteDown(7)
$k::NoteDown(8)

;~~~~~~~~~Middle octave~~~~~~~~~
$q::NoteDown(1)
$w::NoteDown(2)
$e::NoteDown(3)
$r::NoteDown(4)
$t::NoteDown(5)
$y::NoteDown(6)
$u::NoteDown(7)
$i::NoteDown(8)

;~~~~~~~~~High octave~~~~~~~~~
$1::NoteDown(1)
$2::NoteDown(2)
$3::NoteDown(3)
$4::NoteDown(4)
$5::NoteDown(5)
$6::NoteDown(6)
$7::NoteDown(7)
$8::NoteDown(8)
$Numpad1::NoteDown(1)
$Numpad2::NoteDown(2)
$Numpad3::NoteDown(3)
$Numpad4::NoteDown(4)
$Numpad5::NoteDown(5)
$Numpad6::NoteDown(6)
$Numpad7::NoteDown(7)
$Numpad8::NoteDown(8)

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

; Turns sheet music into a list of note groups and octave steps. A group
; holds every note between two octave changes.
ParseSheet(text) {
	downMarks := Chr(0x25BC) . ")[" ; down triangle, ) and [
	upMarks := Chr(0x25B2) . "(]"   ; up triangle, ( and ]
	result := []
	group := ""
	octave := 2      ; octave of the sheet
	gameOctave := 2  ; octave the game will be in
	Loop, Parse, text
	{
		c := A_LoopField
		if InStr(downMarks, c) {
			if (octave > 1)
				octave--
		} else if InStr(upMarks, c) {
			if (octave < 3)
				octave++
		} else if InStr("12345678", c) {
			; Only switch octaves when a note needs it, so marks that cancel
			; out or go past the lowest/highest octave cost no key presses
			if (octave != gameOctave) {
				if (group != "") {
					result.Push(group)
					group := ""
				}
				step := octave < gameOctave ? "9" : "0"
				Loop % Abs(octave - gameOctave)
					result.Push(step)
				gameOctave := octave
			}
			group .= c
		}
	}
	if (group != "")
		result.Push(group)
	return result
}

NoteDown(digit) {
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
	group := Song[Pos]
	if !(i := InStr(group, digit))
		return
	group := SubStr(group, 1, i - 1) . SubStr(group, i + 1)
	Song[Pos] := group
	if (group = "")
		NextGroup()
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

; Moves to the next note group, pressing the octave keys on the way
NextGroup() {
	Pos++
	while (Pos <= Song.Length() && (Song[Pos] == "9" || Song[Pos] == "0")) {
		PressOctave(Song[Pos])
		Pos++
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
