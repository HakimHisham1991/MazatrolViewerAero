0 BEGIN PGM 25062200 MM 
1 ; *************************************
2 ; Project code     : AH03
3 ; Programme Name   : 25062200
4 ; Part Number      : 351-2262-7
5 ; Part description : FCL_KEEPER-2-HOUSING_RH
6 ; Stage/Operation  : OP20
7 ; Programmer       : HAKIM
8 ; Date             : 14/11/2024
9 ; Program Revision : 02
10 ; Work Centre     : 5X-07
11 ; Machine         : DMG DMU60 Evo
12 ; FA              : PROD
13 ; *************************************
14 CYCL DEF 391 PRESET Table ~
    Q305=+1  ;NUMBER IN TABLE ~
    Q381=+0   ;X-PRESET ~
    Q382=+0   ;Y-PRESET ~
    Q383=+0   ;Z-PRESET ~
    Q384=+0  ;A-PRESET ~
    Q385=+0  ;B-PRESET ~
    Q386=+0  ;C-PRESET ~
    Q387=+0    ;machine datum ~
    Q388=+1    ;activate PRESET
15 ; *************************************
16 CYCL DEF 247 DATUM SETTING ~
    Q339=+1  ;DATUM NUMBER
17 ; *************************************
18 CALL PGM TNC:\351-2262-7-OP20\25062201.h
19 M1
20 CALL PGM TNC:\351-2262-7-OP20\25062202.h
21 M1
22 CALL PGM TNC:\351-2262-7-OP20\25062203.h
23 M1
24 CALL PGM TNC:\351-2262-7-OP20\25062204.h
25 ; *************************************
26 L  Z-1 FMAX M91
27 M30
28 END PGM 25062200 MM 
