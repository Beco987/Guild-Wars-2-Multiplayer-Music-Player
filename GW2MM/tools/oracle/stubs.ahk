; Stubs for functions defined in files the oracle does not load (sockets, audio).
; SendTextToClient records what the original would have sent to each client.
SendTextToClient(sText, iSocket) {
    global SentLog
    SentLog .= "SEND`t" iSocket "`t" sText "`n"
}
VA_GetAudioMeter() {
}
VA_IAudioMeterInformation_GetMeteringChannelCount(a, ByRef b) {
}
VA_GetDevicePeriod(a, ByRef b) {
}
VA_IAudioMeterInformation_GetPeakValue(a, ByRef b) {
}
