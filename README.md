# SteamP2PScanner

This is fork of SteamP2PInfo: https://github.com/tremwil/SteamP2PInfo <br>
and SteamP2PInfo-rubiconian: https://github.com/saw44169/SteamP2PInfo-rubiconian <br>
But with the following functions.

## More precise ping monitoring for old steam peer api.
SteamP2PInfo only checks the timing of sending/receiving classicstun packets without their transaction id, so if the receive packet is too much late, it will be taken as the response to the next send packet.<br>
Also, due to missing the transaction id, it cannot detect packet loss and take it as "too much late ping".<br>
SteamP2PScanner now can see the transaction id, so it can detect too late response and packet loss.

## More reliable statistics.
SteamP2PInfo has two statistics: 
1. Average ping(but include high ping due to packet loss)
2. Connection quality(its good to know how much good the conenction is in an instant but none for further analysis).

Instead, SteamP2PScanner has these info:
1. Average ping(exclude packet loss)
2. Packet loss ratio
3. Box plot of pings
4. Bar chart of recent packet loss and pings