global clientSockets := []
global newestSocket := 1
global socketID := 1
global SockID := 0


GuiSize:
    Anchor(htxtDialog, "wh")
    Anchor(htxtInput, "wy")
    Anchor(hbtnSend, "xy")
    Anchor(hlblStatus, "wy", 1)
Return
txtInput:
    /*! This GUI event will be called everytime the text in the Edit control changes. The idea is to send a notification to
    the peer when the user starts/stops typing. We do this using the typing update frame. See the StreamProcessor function
    for more information on frames.
    */
    
    ;Get text entered, if any
    GuiControlGet, sText,, txtInput
    
    ;Create a boolean value
    bTypingUpdate := sText <> ""
Return

btnSend:
    ; Get the text to send
    GuiControlGet, sText,, txtInput
    
    ; Make sure we even have something to send
    If Not sText
        Return
    
    ; Get text length
    iTextLength := StrLen(sText) * 2
    
    for index, clientSocket in clientSockets {
        ; Send the string length frame
        VarSetCapacity(iFrame, 4, 0), NumPut((2 << 16) + iTextLength, iFrame, 0, "UInt")
        If (i := AHKsock_ForceSend(clientSocket, &iFrame, 4)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
    
        ; Send the actual string
        If (i := AHKsock_ForceSend(clientSocket, &sText, iTextLength)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
    }
    ;Data was sent. Add it to the dialog.
    AddDialog(&sText)
        
    ;Clear the Edit control and give focus
    GuiControl,, txtInput
    GuiControl, Focus, txtInput
Return

AddDialog(ptrText, bYou = True) {
    Global htxtDialog
    
    ;Append the interlocutor
    sAppend := bYou ? "You > " : "Peer > "
    InsertText(htxtDialog, &sAppend)
    
    ;Append the new text
    InsertText(htxtDialog, ptrText)
    
    ;Append a new line
    sAppend := "`r`n"
    InsertText(htxtDialog, &sAppend)
    
    ;Scroll to bottom
    SendMessage, 0x0115, 7, 0,, ahk_id %htxtDialog% ;WM_VSCROLL
}

Peer(sEvent, iSocket = 0, sName = 0, sAddr = 0, sPort = 0, ByRef bData = 0, bDataLength = 0) {
    Global iSockets, bExiting, bSameMachine, bCantListen, SocketIDs
    Static iIgnoreDisconnect
    newestSocket := iSocket
    If (sEvent = "ACCEPTED") {
        OutputDebug, % "A client with IP " sAddr " connected!"
        Loop, 5
            {
            if (Ip%A_Index% == "")
                {
                    Ip%A_Index% == %sAddr%
                    output := Ip%A_Index%
                    clientSockets.Push(iSocket)
                    SetTimer, SendSocketID, -1
                    ;Gosub, NewPortMappingGui
                    break
                }
            }
        ;We now have an established connection with a peer
        
        /*! As a fail-safe, we need to make sure that AHKsock_Connect didn't also succeed (which would mean that we are
        already connected to another peer). This should (in theory) never happen because if the CONNECTED event fires before
        an ACCEPTED event, then we would stop listening (see comment block in CONNECTED event), which means that we would
        not be able to accept any other peer. Similarly, if the ACCEPTED event fires before the CONNECTED event, then it
        means that the peer has received the CONNECTED event before an ACCEPTED event, and the same logic applies. 
        
        But to be on the safe side, if somehow both an ACCEPTED and a CONNECTED event occur, the socket reported in the
        second event to occur between the two will be closed, and the other socket (which was reported in the first event)
        will be kept.
        
        This also allows us to prevent cases where the connection attempt was to the same machine, but we could not detect
        it (bSameMachine is False). In such a case, both events will fire (referring to each other's endpoint socket), which
        means that the fail-safe will close the connection when the second of the two events fire.
        */
        ;If (iPeerSocket <> -1) {
        ;    OutputDebug, % "We already have a peer! Disconnecting..."
        ;    AHKsock_Close(iSocket) ;Close the socket
        ;    iIgnoreDisconnect += 1 ;So that we don't react when this peer disconnects
        ;    Return
        ;}
        
        ;Stop listening (see comment block in CONNECTED event)
        ;AHKsock_Listen(27015)
        
        ;Allow input and set focus
        GuiControl, Enable, btnSend
        GuiControl, -ReadOnly, txtInput
        GuiControl, Focus, txtInput
        ;Update status
        GuiControl,, lblStatus, Connected to %sAddr%!
    } If (sEvent = "CONNECTED") {
        
        ;Check if the connection attempt was successful
        If (iSocket = -1) {
            OutputDebug, % "AHKsock_Connect() failed."
            
            ;Check if we are not currently listening, and if we already tried to listen and failed.
            If bCantListen
                ExitApp
            
            OutputDebug, % "Listening for a peer..."
            GuiControl,, lblStatus, Waiting for a peer... ;Update status
            
            ;If the connection attempt was on this computer, we can start listening now since the connect attempt is
            ;over and we thus run no risk of ending up connected to ourselves. 
            If bSameMachine {
                If (i := AHKsock_Listen(27015, "Peer")) {
                    OutputDebug, % "AHKsock_Listen() failed with return value = " i " and ErrorLevel = " ErrorLevel
                    ExitApp
                }
            }
            
            ;The connect attempt failed, but we are now listening for clients. We can leave now.
            Return
            
        } Else OutputDebug, % "AHKsock_Connect() successfully connected on IP " sAddr "."
        
        ;We now have an established connection with a peer
        
        ;This is the same fail-safe as in the ACCEPTED event (see comment block there)
        ;If (iPeerSocket <> -1) {
        ;    OutputDebug, % "We already have a peer! Disconnecting..."
        ;    AHKsock_Close(iSocket) ;Close the socket
        ;    iIgnoreDisconnect += 1 ;So that we don't react when this peer disconnects
        ;    Return
        ;}
        
        /*! We can stop listening now, because we don't want to have more than one peer connnected to us. Note that it's
        possible that we are not even listening anyways (for example, if the connect attempt succeeded, but we weren't
        listening because it's on the same machine). In such a case, calling AHKsock_Listen to stop listening is useless but
        harmless. Therefore, there's no point in checking if we're actually listening before stopping.
        */
       ;AHKsock_Listen(27015)
        
        ;Allow input and set focus
        GuiControl, Enable, btnSend
        GuiControl, -ReadOnly, txtInput
        GuiControl, Focus, txtInput
        ;Update status
        GuiControl,, lblStatus, Connected to %sName%!
    } Else If (sEvent = "DISCONNECTED") {
        ; Remove the client from socket lists
        for index, clientSocket in clientSockets 
        {
            if (clientSocket = iSocket) 
            {
                clientSockets.RemoveAt(index)
                for index, Id in SocketIDs
                {
                    if (Id = iSocket)
                    {
                        SocketIDs[index] := 0
                        break
                    }
                }
                break
            }
        }

        ; Handle disconnection, if needed
        msgbox, % "The client disconnected!"
        
        If (clientSockets.Length() = 0) {
            GuiControl,, lblStatus, Waiting for a peer...
            GuiControl, Disable, btnSend
            GuiControl, +ReadOnly, txtInput
            GuiControl,, txtInput
        }
        Gui, Submit, Nohide
	    Gui, PortMappingGui:Submit
    } Else If (sEvent = "RECEIVED") {
        ; Process data from this client
        StreamProcessor(bData, bDataLength)
    }
}

StreamProcessor(ByRef bNewData = 0, bNewDataLength = -1) {
    Static bPastData, bPastDataLength := 0
    global OneWayTimes
    
    ;Check if we're just erasing any previous data we had
    If (bNewDataLength = -1) {
        If bPastDataLength { ;Check if there's data to erase
            VarSetCapacity(bPastData, 0)
            bPastDataLength := 0
        }
        
        ;OutputDebug, % "SP - Deleting any previous data" ;FOR DEBUGGING PURPOSES ONLY
        Return
    }
    
    ;Check if we have any past data to prepend before entering the processing loop
    If bPastDataLength {
        
        ;OutputDebug, % "SP - Prepending previous data of " bPastDataLength " bytes" ;FOR DEBUGGING PURPOSES ONLY
        
        ;The length of the data to process will be the length of both old and new data combined
        bDataLength := bPastDataLength + bNewDataLength
        
        ;Prep the variable which will hold both the previous data and the new data
        VarSetCapacity(bData, bDataLength)
        
        ;Copy the old data into the beginning of bData, and then the new data right after
        CopyBinData(&bPastData, &bData, bPastDataLength)
        CopyBinData(&bNewData, &bData + bPastDataLength, bNewDataLength)
        
        ;We can now delete any old data we had because we're about to process it
        VarSetCapacity(bPastData, 0) ;Clear the variable to free some memory since it won't be used
        bPastDataLength := 0 ;Reset the value
        
        ;Now bData contains the newly received data as well as any previous data that could not be fully processed before
        
        ;Set the data pointer to the new data we just created
        bDataPointer := &bData
        
        /*! The advantage of using a data pointer is so that the code that follows after can work regardless of whether
        the data to process is in bNewData (if we had nothing to prepend), or in bData (if we had to create it to
        prepend some past data). The variable bDataLength holds the length of the data to which bDataPointer points.
        */        
        
    ;Set the data pointer to the newly arrived data
    } Else bDataPointer := &bNewData, bDataLength := bNewDataLength
    
    ;Now bDataPointer points to the data to process, whether it be only the new data that arrived, or old and new data which
    ;have been concatenated. The length of the data to which bDataPointer points is in bDataLength.
    
    ;Check if we have at least one frame to process
    If (bDataLength < 4) {
        
        ;OutputDebug, % "SP - Less than a frame to process. Saving " bDataLength " bytes and leaving" ;FOR DEBUGGING PURPOSES ONLY
        
        ;We only have part of one frame. Keep what we have and leave.
        VarSetCapacity(bPastData, bPastDataLength := bDataLength)
        CopyBinData(bDataPointer, &bPastData, bDataLength)
        Return
    }
    
    ;Start processing the stream of frames
    iOffset := 0
    While (iOffset + 4 <= bDataLength) {
        
        ;Get frame type and data
        iFrame     := NumGet(bDataPointer+0, iOffset, "UInt")
        iFrameType := iFrame >> 16 ;High-word
        iFrameData := iFrame & 0xFFFF ;Low-word        
        
        /*! The type of the frame is in the high-word, and the data of the frame is in the low-word:
            - Typing update frames have a high-word of 1, and a boolean value in the low-word indicating typing status
            - String length frames have a high-word of 2, and the length of the string that follows in the low-word
        */
        
        ;Check what kind of frame it is
        If (iFrameType = 1) { ;It's a typing update frame
            GuiControl,, lblStatus, % iFrameData ? "Peer is typing..." : "Connected!" ;Update
            iOffset += 4 ;Increase the offset to be at the beginning of the next frame
        } Else If (iFrameType = 2) { ;It's a string length frame
            
            ;Check if the whole string is present
            ;We do + 4 because the string starts right after the frame we're currently processing
            If Not (iFrameData <= bDataLength - (iOffset + 4)) {
                
                ;OutputDebug, % "SP - Part of string missing. Saving " (bDataLength - iOffset) " bytes and leaving" ;FOR DEBUGGING PURPOSES ONLY
                
                ;We only have part of a string. Keep what we have (including the string length frame) and leave.
                ;We include the string length frame so that this loop will be able to handle it the next time
                ;it processes the same data but with more of the string at the end
                VarSetCapacity(bPastData, bPastDataLength := bDataLength - iOffset)
                CopyBinData(bDataPointer + iOffset, &bPastData, bDataLength - iOffset)
                Return
            }
            
            ;Increase the offset to where the string starts (right after the frame we're processing)
            iOffset += 4
            
            ;Get the string and add to dialog
            VarSetCapacity(sTextData, iFrameData + 2, 0)
            CopyBinData(bDataPointer + iOffset, &sTextData, iFrameData)

            ;Add the string to the dialog textbox
            AddDialog(&sTextData, False)
            
            ;Move the offset by the length of the string to be at the beginning of the next frame
            iOffset += iFrameData

            if (InStr(sTextData, "Play")) 
            {
                parts := StrSplit(sTextData, ";")  ; Split the string by commas
                global ClientData .= parts[2]
            }

            if (InStr(sTextData, "Stop")) 
            {
                SetTimer, SendStop, -1
            }

            if (InStr(sTextData, "Tick_Count")) 
            {
                parts := StrSplit(sTextData, ",")
                Sock := parts[2]
                Sock := StrReplace(Sock, "Socket")
                T2 := A_TickCount
                diff := T2 - T1
                diff /= 2
                TickCounts[Sock] := parts[3] - diff
            }

            if (InStr(sTextData, "Socket_Ping"))
            {
                parts := StrSplit(sTextData, ",")
                Sock := parts[2]
                Sock := StrReplace(Sock, "Socket")
                varName := "Ip" . Sock . "GW2"
                SocketPing := parts[3]
                %varName% = %SocketPing%
                GuiControl, PortMappingGui:, %varName%, %SocketPing%
                Pings[Sock] := SocketPing
            }

            if (InStr(sTextData, "Return_Ping")) 
            {
                parts := StrSplit(sTextData, ",")
                Sock := parts[2]
                Sock := StrReplace(Sock, "Socket")
                global T2 := A_TickCount
                diff := T2 - T1
                diff /= 2
                OneWayTimes.Push(diff)
            }

            if (InStr(sTextData, "Return_Start_Ping")) 
            {
                parts := StrSplit(sTextData, ",")
                Sock := parts[1]
                Sock := StrReplace(Sock, "Socket")
                global SockID := SocketIDs[Sock]
                SetTimer, SendReturnStartPing, -1
            }

            if (InStr(sTextData, "Return_Start_Half_Ping")) 
            {
                parts := StrSplit(sTextData, ",")
                Sock := parts[1]
                Sock := StrReplace(Sock, "Socket")
                global SockID := SocketIDs[Sock]
                SetTimer, SendReturnStartHalfPing, -1
            }

            if (InStr(sTextData, "StartSong")) 
            {
                parts := StrSplit(sTextData, ",")  ; Split the string by commas
                global DefaultTempo := parts[2]
                global FromClient := true
                Sleep, 250
                global Stop := false
                GoSub SongPlayer
            }
        }
    }

    ;Check if there are bytes part of an incomplete frame at the end of the stream that could not be processed
    If (n := (bDataLength - iOffset)) And (n < 4) {
        
        ;OutputDebug, % "SP - Less than a frame left at the end of the stream. Saving " n " bytes and leaving" ;FOR DEBUGGING PURPOSES ONLY
        
        ;We only have part of one frame. Keep what we have and leave.
        VarSetCapacity(bPastData, bPastDataLength := n)
        CopyBinData(bDataPointer + iOffset, &bPastData, n)
        Return
    }
}

AHKsockErrors(iError, iSocket) {
    OutputDebug, % "Error " iError " with error code = " ErrorLevel ((iSocket <> -1) ? " on socket " iSocket "." : ".") 
}

CopyBinData(ptrSource, ptrDestination, iLength) {
    If iLength ;Only do it if there's anything to copy
        DllCall("RtlMoveMemory", "Ptr", ptrDestination, "Ptr", ptrSource, "UInt", iLength)
}

InsertText(hEdit, ptrText, iPos = -1) {
    
    If (iPos = -1) {
        SendMessage, 0x000E, 0, 0,, ahk_id %hEdit% ;WM_GETTEXTLENGTH
        iPos := ErrorLevel
    }
    
    SendMessage, 0x00B1, iPos, iPos,, ahk_id %hEdit% ;EM_SETSEL
    SendMessage, 0x00C2, False, ptrText,, ahk_id %hEdit% ;EM_REPLACESEL
}

Anchor(i, a = "", r = false) {
	static c, cs = 12, cx = 255, cl = 0, g, gs = 8, gl = 0, gpi, gw, gh, z = 0, k = 0xffff, ptr
	If z = 0
		VarSetCapacity(g, gs * 99, 0), VarSetCapacity(c, cs * cx, 0), ptr := A_PtrSize ? "Ptr" : "UInt", z := true
	If (!WinExist("ahk_id" . i)) {
		GuiControlGet, t, Hwnd, %i%
		If ErrorLevel = 0
			i := t
		Else ControlGet, i, Hwnd, , %i%
	}
	VarSetCapacity(gi, 68, 0), DllCall("GetWindowInfo", "UInt", gp := DllCall("GetParent", "UInt", i), ptr, &gi)
		, giw := NumGet(gi, 28, "Int") - NumGet(gi, 20, "Int"), gih := NumGet(gi, 32, "Int") - NumGet(gi, 24, "Int")
	If (gp != gpi) {
		gpi := gp
		Loop, %gl%
			If (NumGet(g, cb := gs * (A_Index - 1)) == gp, "UInt") {
				gw := NumGet(g, cb + 4, "Short"), gh := NumGet(g, cb + 6, "Short"), gf := 1
				Break
			}
		If (!gf)
			NumPut(gp, g, gl, "UInt"), NumPut(gw := giw, g, gl + 4, "Short"), NumPut(gh := gih, g, gl + 6, "Short"), gl += gs
	}
	ControlGetPos, dx, dy, dw, dh, , ahk_id %i%
	Loop, %cl%
		If (NumGet(c, cb := cs * (A_Index - 1), "UInt") == i) {
			If a =
			{
				cf = 1
				Break
			}
			giw -= gw, gih -= gh, as := 1, dx := NumGet(c, cb + 4, "Short"), dy := NumGet(c, cb + 6, "Short")
				, cw := dw, dw := NumGet(c, cb + 8, "Short"), ch := dh, dh := NumGet(c, cb + 10, "Short")
			Loop, Parse, a, xywh
				If A_Index > 1
					av := SubStr(a, as, 1), as += 1 + StrLen(A_LoopField)
						, d%av% += (InStr("yh", av) ? gih : giw) * (A_LoopField + 0 ? A_LoopField : 1)
			DllCall("SetWindowPos", "UInt", i, "UInt", 0, "Int", dx, "Int", dy
				, "Int", InStr(a, "w") ? dw : cw, "Int", InStr(a, "h") ? dh : ch, "Int", 4)
			If r != 0
				DllCall("RedrawWindow", "UInt", i, "UInt", 0, "UInt", 0, "UInt", 0x0101) ; RDW_UPDATENOW | RDW_INVALIDATE
			Return
		}
	If cf != 1
		cb := cl, cl += cs
	bx := NumGet(gi, 48, "UInt"), by := NumGet(gi, 16, "Int") - NumGet(gi, 8, "Int") - gih - NumGet(gi, 52, "UInt")
	If cf = 1
		dw -= giw - gw, dh -= gih - gh
	NumPut(i, c, cb, "UInt"), NumPut(dx - bx, c, cb + 4, "Short"), NumPut(dy - by, c, cb + 6, "Short")
		, NumPut(dw, c, cb + 8, "Short"), NumPut(dh, c, cb + 10, "Short")
	Return, true
}

SendSocketID:
    sText := "Socket,Socket"
    SocketID := ""
	for i, v in SocketIDs
    {
        if (v == 0)
        {
            SocketIDs[i] := newestSocket
            SocketID := i
            break
        }
    }
    sText .= SocketID
    iTextLength := StrLen(sText) * 2
    ; First send a string length frame: high-word is 2, low-word is the length of the string
    VarSetCapacity(iFrame, 4, 0), NumPut((2 << 16) + iTextLength, iFrame, 0, "UInt")
        If (i := AHKsock_ForceSend(newestSocket, &iFrame, 4)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
    
        ; Send the actual string
        If (i := AHKsock_ForceSend(newestSocket, &sText, iTextLength)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
Return

SendReturnPing:
    sText := "Return_Ping"
    global SockID
    iTextLength := StrLen(sText) * 2
    ; First send a string length frame: high-word is 2, low-word is the length of the string
    VarSetCapacity(iFrame, 4, 0), NumPut((2 << 16) + iTextLength, iFrame, 0, "UInt")
        If (i := AHKsock_ForceSend(SockID, &iFrame, 4)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
    
        ; Send the actual string
        If (i := AHKsock_ForceSend(SockID, &sText, iTextLength)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
Return

SendReturnStartPing:
    sText := "Return_Start_Ping"
    global SockID
    iTextLength := StrLen(sText) * 2
    ; First send a string length frame: high-word is 2, low-word is the length of the string
    VarSetCapacity(iFrame, 4, 0), NumPut((2 << 16) + iTextLength, iFrame, 0, "UInt")
        If (i := AHKsock_ForceSend(SockID, &iFrame, 4)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
    
        ; Send the actual string
        If (i := AHKsock_ForceSend(SockID, &sText, iTextLength)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
Return

SendStop:
    Gui, Show,, GW2MM
    global Stop := true
    global FromClient := false
    for i, v in SocketIDs
    {
        if ((v != 0) && (v != 1))
        {
            sText := "Stop"
            SendTextToClient(sText, v)
        }
    }
    Gosub, RefreshPlayers
    SetTimer, SendStop, Off
Return

SendReturnStartHalfPing:
    sText := "Return_Start_Half_Ping"
    global SockID
    iTextLength := StrLen(sText) * 2
    ; First send a string length frame: high-word is 2, low-word is the length of the string
    VarSetCapacity(iFrame, 4, 0), NumPut((2 << 16) + iTextLength, iFrame, 0, "UInt")
        If (i := AHKsock_ForceSend(SockID, &iFrame, 4)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
    
        ; Send the actual string
        If (i := AHKsock_ForceSend(SockID, &sText, iTextLength)) {
            msgbox, % "AHKsock_ForceSend failed with return value = " i " and error code = " ErrorLevel " at line " A_LineNumber
            ExitApp
        }
Return

SendTextToClient(sText, iSocket) {
    iTextLength := StrLen(sText) * 2

    VarSetCapacity(iFrame, 4, 0)
    NumPut((2 << 16) + iTextLength, iFrame, 0, "UInt")

    ; non-blocking send (replace ForceSend with Send)
    AHKsock_Send(iSocket, &iFrame, 4)
    AHKsock_Send(iSocket, &sText, iTextLength)
}

