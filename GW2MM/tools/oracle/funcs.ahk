; Oracle harness: runs functions from the ORIGINAL (unmodified) AHK sources against
; test vectors and writes their results. Usage: AutoHotkeyU64.exe funcs.ahk in.txt out.txt
; Each input line:  id <TAB> fn <TAB> arg1 <TAB> arg2 ...   (arrays encoded as "count:item|item|...")
#NoEnv
StringCaseSense, On
SetBatchLines, -1
global Delay := 0
inFile := A_Args[1], outFile := A_Args[2]
FileDelete, %outFile%
out := ""
Loop, Read, %inFile%
{
    f := StrSplit(A_LoopReadLine, "`t")
    id := f[1], fn := f[2]
    if (fn = "Combine") {
        r := Combine(Dec(f[3]), Dec(f[4]), Dec(f[5]), Dec(f[6]))
        out .= id "`t" Enc(r.num) "`t" Enc(r.alpha) "`n"
    } else if (fn = "CombineFlags") {
        r := CombineFlags(Dec(f[3]), Dec(f[4]), Dec(f[5]), Dec(f[6]), false)
        out .= id "`t" Enc(r.num) "`t" Enc(r.alpha) "`n"
    } else if (fn = "AddSwaps") {
        r := AddSwaps(Dec(f[3]))
        out .= id "`t" Enc(r) "`n"
    } else if (fn = "OpportunisticSwaps") {
        Delay := f[5]
        r := OpportunisticSwaps(Dec(f[3]), Dec(f[4]))
        out .= id "`t" Enc(r.num) "`t" Enc(r.alpha) "`n"
    } else if (fn = "RemoveOutliers") {
        r := RemoveOutliers(Dec(f[3]))
        out .= id "`t" Enc(r) "`n"
    }
}
FileAppend, %out%, %outFile%, UTF-8
ExitApp

Dec(s) {
    p := InStr(s, ":")
    n := SubStr(s, 1, p - 1)
    rest := SubStr(s, p + 1)
    a := []
    if (n = 0)
        return a
    Loop, Parse, rest, |
        a.Push(A_LoopField)
    return a
}

Enc(a) {
    s := ""
    for k, v in a
        s .= (k = 1 ? "" : "|") v
    return a.Length() ":" s
}

#Include ..\..\..\Midi Player - Server\scripts\Functions.ahk
#Include ..\..\..\Midi Player - Server\scripts\ConnectButtons.ahk
#Include stubs.ahk
