NewPortMappingGui:
	Gui,PortMappingGui:Add,Text,x0 y0,IP :
	Gui,PortMappingGui:Add,Text,x0 y40,Track 1 :
	Gui,PortMappingGui:Add,Text,x0 y60,Track 2 :
	Gui,PortMappingGui:Add,Text,x0 y80,Track 3 :
	Gui,PortMappingGui:Add,Text,x0 y100,Track 4 :
	Gui,PortMappingGui:Add,Text,x0 y120,Track 5 :

	Gui,PortMappingGui:Add,Text,x50 y0,%Ip1%
	Gui,PortMappingGui:Add,CheckBox, x65 y40 Checked%Ip11%
	Gui,PortMappingGui:Add,CheckBox, x65 y60 Checked%Ip12%
	Gui,PortMappingGui:Add,CheckBox, x65 y80 Checked%Ip13%
	Gui,PortMappingGui:Add,CheckBox, x65 y100 Checked%Ip14%
	Gui,PortMappingGui:Add,CheckBox, x65 y120 Checked%Ip15%
	if (Ip2 != "")
		{
		Gui,PortMappingGui:Add,Text,x150 y0,%Ip2%
		Gui,PortMappingGui:Add,CheckBox, x165 y40 Checked%Ip21%
		Gui,PortMappingGui:Add,CheckBox, x165 y60 Checked%Ip22%
		Gui,PortMappingGui:Add,CheckBox, x165 y80 Checked%Ip23%
		Gui,PortMappingGui:Add,CheckBox, x165 y100 Checked%Ip24%
		Gui,PortMappingGui:Add,CheckBox, x165 y120 Checked%Ip25%	
		}
	if (Ip3 != "")
		{
		Gui,PortMappingGui:Add,Text,x250 y0,%Ip3%
		Gui,PortMappingGui:Add,CheckBox, x265 y40 Checked%Ip31%
		Gui,PortMappingGui:Add,CheckBox, x265 y60 Checked%Ip32%
		Gui,PortMappingGui:Add,CheckBox, x265 y80 Checked%Ip33%
		Gui,PortMappingGui:Add,CheckBox, x265 y100 Checked%Ip34%
		Gui,PortMappingGui:Add,CheckBox, x265 y120 Checked%Ip35%	
		}
	if (Ip4 != "")
		{
		Gui,PortMappingGui:Add,Text,x350 y0,%Ip4%
		Gui,PortMappingGui:Add,CheckBox, x365 y40 Checked%Ip41%
		Gui,PortMappingGui:Add,CheckBox, x365 y60 Checked%Ip42%
		Gui,PortMappingGui:Add,CheckBox, x365 y80 Checked%Ip43%
		Gui,PortMappingGui:Add,CheckBox, x365 y100 Checked%Ip44%
		Gui,PortMappingGui:Add,CheckBox, x365 y120 Checked%Ip45%	
		}
	if (Ip5 != "")
		{
		Gui,PortMappingGui:Add,Text,x450 y0,%Ip5%
		Gui,PortMappingGui:Add,CheckBox, x465 y40 Checked%Ip51%
		Gui,PortMappingGui:Add,CheckBox, x465 y60 Checked%Ip52%
		Gui,PortMappingGui:Add,CheckBox, x465 y80 Checked%Ip53%
		Gui,PortMappingGui:Add,CheckBox, x465 y100 Checked%Ip54%
		Gui,PortMappingGui:Add,CheckBox, x465 y120 Checked%Ip55%	
		}
	Gui PortMappingGui:Add,Button, x250 y140 w50 h40 gPortMappingGuiSaveButton, Save
return

ConnectedPlayer:
if (Ip11 == 1 || Ip12 == 1 || Ip13 == 1 || Ip14 == 1 || Ip15 == 1)
	{

	}
return
