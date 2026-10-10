#SingleInstance Force
#NoEnv
;------------------------------- Controls ------------------------------;
F11::
	global Stop := false
	Gosub, Songplayer
return

F4::
	Gosub, RefreshPlayers
	ExitApp
return

F5::
	WinWaitActive, Guild Wars 2
	WinSetTitle, Guild Wars 2,,Player1
return

F6::
	WinWaitActive, Guild Wars 2
	WinSetTitle, Guild Wars 2,,Player2
return

F7::
	WinWaitActive, Guild Wars 2
	WinSetTitle, Guild Wars 2,,Player3
return

F8::
	WinWaitActive, Guild Wars 2
	WinSetTitle, Guild Wars 2,,Player4
return

F9::
	WinWaitActive, Guild Wars 2
	WinSetTitle, Guild Wars 2,,Player5
return