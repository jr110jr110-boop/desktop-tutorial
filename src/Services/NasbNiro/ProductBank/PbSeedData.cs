namespace Services.NasbNiro.ProductBank
{
    /// <summary>
    /// Demonstration data of the company's own FY product, transcribed from its documents: the BOM (87 rows), the seven OPC charts, the work breakdown of the MS Project plan
    /// and a few items of the warehouse lists (codes and names as written there). Signature names of the documents are not copied: the signatures are roles.
    /// Line format of the BOM: H|level|heading  and  L|item no|name|part number|qty|standard|material|diameter|length|width|thickness|net weight|gross weight|MPL or PL|description.
    /// </summary>
    internal static class PbSeedData
    {
        public const string Bom = """
H|0|Casing
H|1|Wheel case
L|1|Wheel case FY R01|16.11.01.001.2|1|A216|WCB|||||137|157.55|MPL|ریخته گری
L|2|Casing Cover FY|16.11.01.002.2|1|A216|WCB|||||36|41.40|MPL|ریخته گری
L|3|Governor housing FY|16.11.01.003.2|1|A216|WCB|||||16.828|19.35|MPL|ریخته گری
L|4|Bearing cap FY|16.11.01.004.2|1|A216|WCB|||||6|6.90|MPL|ریخته گری
L|5|G.O.V Housing End UH|16.13.01.005.2|1||GGG40|||||6.324|7.27|MPL|ریخته گری
L|6|G.O.V Housing End LH|16.13.01.006.2|1||GGG40|||||6.324|7.27|MPL|ریخته گری
H|1|STD
L|7|Hex. socket head cap screw 3/8"-16 UNC1"|70.04.06.052.1|8|ASME B18.3 (Imperial)|Alloy Steel Gr. B7|||||||PL|for governor housing end to governor; اندازه: 3/8"-16 UNC × 1"
L|8|Plug 1/4 ''NPT|72.04.01.007.1|3|ASME B16.11 (Imperial)|درپوش|||||||PL|for Oil Feed Line on Casing &Casing cover (Hex. Head Plug); اندازه: 1/4" NPT
L|9|Hex head screw 1/2"-13 UNCx2 3/4"|70.01.06.251.1|8|ASME B18.2 (Imperial)|Alloy Steel Gr. B7|||||||PL|Governor housing & Bearing cap screw; اندازه: 1/2"-13 UNC × 2 3/4"
L|10|Stud Bolt 1/2"-13 UNCx2 1/2"|70.02.06.252.1|30|ASME B18.31.2 (Imperial)|Alloy Steel Gr. B7|||||||PL|Casing cover (Tap-End Stud 1.5D Engagement Full Body); اندازه: 1/2"-13 UNC × 2 1/2"
L|11|Heavy Hex Nut 1/2" UNC|71.01.06.252.1|30|ASME B18.2.2 (Imperial)|Alloy Steel Gr. B7|||||||PL|Type 2H; اندازه: 1/2"-13 UNC
L|12|Plain Washer 1/2"|74.05.01.001.1|38|ASME B18.22.1 (Imperial)|واشر استیل|||||||PL|Casing cover& Governor & Bearing housing; OD: 1.062"
L|13|Stud 5/8"-11 UNCx3 3/4"|70.02.06.253.1|8|ASME B18.31.2 (Imperial)|Alloy Steel Grade B7|||||||PL|for Exhast Blind Flange (Clamping type stud Full Body); اندازه: 5/8"-11 UNC × 3 3/4"
L|14|Hex Nut 5/8" UNC|71.01.06.253.1|16|ASME B18.2.2 (Imperial)|Alloy Steel Grade B7|||||||PL|for Exhast Blind Flange (Heavy Hex); اندازه: 5/8"-11 UNC
L|15|Plug 1/2" NPT|72.04.01.001.1|6|ASME B16.11 (Imperial)|درپوش|||||||PL|اندازه: 1/2" NPT
L|16|Dowel pin ɸ8h735|73.02.02.005.1|4|ISO 8735|پین سختکاری شده رزوه دار|8|35|||||PL|
L|17|Plain Washer 5/16"|74.05.01.002.1|8|ASME B18.22.1 (Imperial)|واشر استیل|||||||PL|for Governor housing end to governor; OD: 0.688"
L|18|Dowel pin ɸ6h730|73.02.02.003.1|2|ISO 8735|پین سختکاری شده رزوه دار|6|30|||||PL|for DE Hosing
L|19|Plug 1 1/2" G Hex. Head|72.04.01.005.1|1|ASME B16.11 (Imperial)|درپوش|||||||PL|for Gov. Disk Adj. hole; اندازه: 1 1/2" G
L|20|Plug 1/2" G Hex. Head|72.04.01.001.1|4|ASME B16.11 (Imperial)|درپوش|||||||PL|اندازه: 1/2" G
L|21|Plug 3/8" G Hex. Head|72.04.01.008.1|2|ASME B16.11 (Imperial)|درپوش|||||||PL|اندازه: 3/8" G
L|23|Eye bolt M16|70.10.01.101.1|1|JIS B 1168|Carbon steel|||||||PL|اندازه: M16
H|0|Bearing
H|1|Coup.end Bearing
L|24|Coupling end Bearing FY|16.11.04.002.2|1|C93200|Phosphor Bronze|100|120|||2.298|2.298|MPL|
L|25|Oil Ring FY|16.11.04.003.2|2|C93200|Phosphor Bronze|110|15|||0.043|0.086|MPL|
H|1|STD
L|26|Dowel Pin ɸ6m6x26|73.02.02.004.1|2|ISO 8734|STD|6|25|||||PL|اندازه: 6m6
L|27|Hex. socket head cap screw 1/4"-20 UNCx7/8"|70.04.07.151.1|2|ASME B18.2 (Imperial)|SAE-J429 Gr. 8|||||||PL|Bearing screw; اندازه: 1/4"-20 UNC × 7/8"
H|1|GOV.end Bearing
L|27|Governor end Bearing FY|16.11.04.001.2|1|C93200|Phosphor Bronze|100|110|||1.845|1.845|MPL|
L|28|Oil Ring FY|16.11.04.003.2|2|C93200|Phosphor Bronze|110|15|||0.043|0.086|MPL|
H|1|STD
L|29|Dowel Pin ɸ6m6x26|73.02.02.004.1|2|ISO 8734|STD|6|25|||||PL|اندازه: 6m6
L|30|Hex. socket head cap screw 1/4"-20 UNCx7/8"|70.04.07.151.1|2|ASME B18.2 (Imperial)|SAE-J429 Gr. 8|||||||PL|Bearing screw; اندازه: 1/4"-20 UNC × 7/8"
H|0|Carbon Ring
L|31|Packing ring spring stop|16.11.01.012.2.S1|8|ASTM A276 gr 420 or AISI 420|SS420 (تسمه)|||||0.003|0.005|MPL|خرید متریال توسط پیمانکار؛ شماره قطعه در BOM کاغذی 16.11.01.012.2 است و برای یکتایی موقتاً .S1 دارد
L|32|Carbon Packing ring|16.11.01.012.2|8|A216|EK20|||||||MPL|خرید متریال توسط پیمانکار
L|33|Carbon Packing ring spring|16.11.01.012.2.S2|8|ASTM A276 gr 302 or AISI 302|SS302|||||0.03||MPL|خرید متریال توسط پیمانکار؛ شماره قطعه در BOM کاغذی 16.11.01.012.2 است و برای یکتایی موقتاً .S2 دارد
H|0|General
L|34|Hand valve plug|48003|2|ASTM A276 gr 420 or AISI 420|round bar - SS420|65|140|||0.54|1|PL|
L|35|Inspection Plug|48002|4|ASTM A276 gr 420 or AISI 420|شش پر|||||0.125|0.15|PL|SW30 × 170
L|36|Spacer Female Hex|48006|2|DIN 7168f|شش پر|||||||PL|SW16 × 85
L|37|Gov. End Linkage|48012|1|DIN 17100|تسمه 15x30||110|30|15|||PL|
L|38|Coupling 1 to 1/2"|48014|1|ASTM276- Gr 316 or AISI 316|شش پر|||||0.3|0.7|PL|SW41 × 60؛ در صورت عدم موجودی، سایز بزرگتر تهیه گردد
H|1|STD
L|39|plug 1/2" NPT|72.04.01.001.1|4|ASME B16.11 (Imperial)|درپوش|||||||PL|اندازه: 1/2" NPT
H|0|Gland Case
L|40|Gland case LH FY|16.11.01.010.2|2|DIN 1693|GGG40|||||1.484|1.71|MPL|ریخته گری
L|41|Gland case UH FY|16.11.01.011.2|2|DIN 1693|GGG40|||||1.49|1.71|MPL|ریخته گری
L|45|Gland Case Tube 3FY|16.13.01.013.2|4||Carbon steel|||||0.223|0.892|MPL|دو قطعه خام: SW28 طول 50 و Pipe 1/2" sch STD طول 90
H|1|STD
L|46|Stud Bolt 3/8"-16 UNCx3"|70.02.06.254.1|4|ASME B18.31.2 (Imperial)|ASTM A193 B7|||||||PL|Continuous Thread Stud; اندازه: 3/8"-16 UNC × 3"
L|47|Nut 3/8˝-16 UNC|71.01.06.051.1|4|ASME B18.2.2 (Imperial)|ASTM A193 B7|||||||PL|اندازه: 3/8"-16 UNC
L|48|Dowel pin ɸ6m645|73.02.02.006.1|4|ISO 8735|پین سختکاری شده رزوه دار|6|45|||||PL|
H|0|Gov.disk
L|49|Governor Disk 3FY C & CCW|16.13.02.211.2|1|AISI 4140 (1.7227)|گرده-Mo-40|165|75|||2.458|2.458|PL|
L|50|Emerg weight screw 3FY|16.13.02.216.2|1|ASTM A276 gr 420 or AISI 420|SS420-round bar|30|60|||0.042|0.042|PL|
L|51|Emerg Spring adj stud 3FY|16.13.02.213.2|1|ASTM A276 gr 420 or AISI 420|چهار پهلو-SS420||60|12|12|0.02|0.02|PL|
L|52|Emergency Spring Seat 3FY|16.13.02.014.2|1|ASTM A276 gr 420 or AISI 420|round bar|20|15|||0.014|0.014|PL|
L|53|Emerg Gov weight 3FY CW & CCW|16.13.02.215.2|1||c93200 Phosphor Bronze (تسمه)||80|50|20|0.158|0.158|PL|
L|55|Emergency Weight Stop Pin 3FY|16.13.02.017.2|1|ASTM A276 gr 420 or AISI 420|SS 420-round bar|15|30|||0.01|0.01|PL|
L|56|Emerg Weight spring 3FY|16.13.02.018.2|1|ASTM A276 gr 302 or AISI 302|فنر مطابق نقشه-SS 302|||||0.017|0.017|PL|خرید متریال توسط پیمانکار
H|1|STD
L|57|Hex. Nut 5/16"-18 UNC|71.01.06.052.1|2|ASME B18.2.2 (Imperial)|ASTM A193 B7|||||||PL|اندازه: 5/16"-18 UNC
L|58|Hex. Nut 3/8"-16 UNC|71.01.06.051.1|1|ASME B18.2.2 (Imperial)|ASTM A193 B7|||||||PL|اندازه: 3/8"-16 UNC
H|0|Hand Valve
L|59|Hand valve Body 3FY|16.13.03.001.2|1|DIN1.7225(MO40)|round bar|80|115|||0.947|0.947|PL|
L|60|Hand Valve Stem 3FY|16.13.03.002.2|1|AISI 420|round bar|30|235|||0.475|0.475|PL|
L|61|Hand Valve Plunger 3FY|16.13.03.003.2|1|AISI304(SS304)|round bar|45|50|||0.196|0.196|PL|
L|62|Hand valve Packing Follower 3FY|16.13.03.004.2|1|AISI 420|round bar|35|30|||0.04|0.04|PL|
L|63|Hand valve Packing nut 3FY|16.13.03.005.2|1|AISI 420|شش پر SW48||35|||0.196|0.196|PL|SW48
L|64|Hand Valve Graphite Packing 3FY|16.13.03.006.2|8||PTFE (25%Carbon)|28|||4||0|PL|
L|65|Hand Valve Plunger Pin|16.13.03.007.2|1||||||||0|PL|
L|66|Plug&Hand Valve gasket|16.13.03.008.2|3||واشر مسی COPPER||||2|||PL|ID:51 OD:60
H|0|Nozzle
L|67|Steam jet 3FY (9.5-12mm)|16.13.05.005.2|4|ASTM A276 gr 420 or AISI 420|شش پر||70|||0.09|0.36|MPL|SW27
L|68|Steamjet Body 3FY CW|16.13.05.102.2|4|ASTM A276 gr 420 or AISI 420|round bar|60|90|||0.676|2.704|PL|بسته به جهت توربین یکی انتخاب شود
L|68|Steamjet Body 3FY CCW|16.13.05.002.2|4|ASTM A276 gr 420 or AISI 420|round bar|60|130|||0.93|3.72|PL|بسته به جهت توربین یکی انتخاب شود
L|69|Jet Body Holder 3FY|16.13.05.003.2|4|ASTM B144-C93200|round bar Phosphor Bronze|45|60|||0.21|0.84|PL|
L|70|Steam Jet Gasket 3FY|16.13.05.001.2|4|ASTM B152|Washer COPPER||||0.5|0.001|0.004|PL|ID25.4 OD30
L|71|Jet Body gascket 3FY|16.13.05.004.2|12|ASTM B152|Washer COPPER||||0.5|0.004|0.048|PL|ID38 OD50
H|0|Reversing Chamber
L|72|FY & 3FY Reversing chamber|16.13.09.101.2|4|ASTM B505|AL-Bronze (C95400)|||||1.35|1.55|PL|ریخته گری
H|1|STD
L|75|Socket head cap screws M8*25|90088|16|ANSI B18.6.3|Stainless Steel (A2)|||||||PL|اندازه: M8 × 25
H|0|Rotor
L|76|Shaft FY|16.11.02.001.2|1|VCN150 ASTM 1.6285|round bar VCN150|60|950|||12|12|MPL|برای انجام تست 100mm اضافه در نظر گرفته شده
L|78|WHEEL FY|16.11.02.002.2|1|DIN 1.7225 Or AISI 4140|round bar Mo40|480|80|||31.5|31.5|MPL|
L|79|Wheel shaft nut FY|16.11.02.003.2|1|DIN 1.7225 Or AISI 4140|round bar Mo40|100|50|||0.5|0.5|MPL|
L|80|Steam Bunter Collar Small FY R02|16.13.02.004.2|1|C93200|round bar Phosphor Bronze|110|20|||0.12|0.12|MPL|
L|81|Oil deflector 3FY R01|16.13.02.005.2|1|ASTM A276 gr 420 or AISI 420|round bar SS420|100|15|||0.223|0.223|MPL|
L|82|Steam Bunter Collar Large 3FY R02|16.13.02.006.2|2|C93200|میلگرد Phosphor Bronze|110|50|||0.283|0.566|MPL|
L|83|Gov spring adj nut 3FY R01|16.13.02.007.2|1|ASTM A276 gr 420 or AISI 420|SW-50|50|50|||0.311|0.311|MPL|
L|84|Copper Piece 3FY R01|16.13.02.008.2|2|ASTM B187|پین مسی|6|15|||0.001|0.002|MPL|
L|85|Governor Disk Hold Bush 3FY R01|16.13.02.009.2|1|AISI 420|round bar|50|115|||0.6|0.6|MPL|
L|86|Ball bearing Spacer 3FY|16.13.02.010.2|1|ASTM A276 gr 420 or AISI 420|round bar SS420|45|20|||0.09|0.09|MPL|
H|1|STD
L|87|Parallel key form A|78.01.03.002.1|1|DIN 6885|STD Key||36|10|8|||MPL|Wheel Key
L|88|Parallel key form AB|78.01.03.004.1|1|DIN 6885|STD Key||80|14|9|||MPL|Coupling Key
L|89|Parallel key form A|78.01.03.003.1|1|DIN 6885|STD Key||20|5|5|||MPL|Gov. disk key
L|90|Ball thrust bearing|79.01.01.001.1|1|SKF- 7207|Bearing steel|||||||MPL|
L|91|Hex.socket head set screw1/4"-20 UNCx5/16"|70.03.06.001.1|9|ASME B18.3|SAE-J429 Grade 8|||||||MPL|Oval Point Type; اندازه: 1/4"-20 UNC × 5/16"
L|92|Hex. socket head set screw M6x6|70.03.01.001.1|1|DIN 916|SAE-J429 Grade 8|||||||MPL|Oval Point Type; اندازه: M6 × 6
L|93|Parallel key form AB|78.01.03.005.1|1|DIN 6885|STD Key||25|5|5|||MPL|
L|94|Ball Bearing|79.01.02.001.1|1|SKF-6811|Bearing steel|||||||MPL|OD:72 ID:55
""";
        public const string Opc = """
R|16.13.01.000.2|Casing|CO-00
P|16.11.01.001.2|Wheel case
S|CA HT SB QC:DIM SP TH:TENSILE+HARDNESS TR QC:VT ML DR QC:DIM+UT TR
P|16.11.01.002.2|Casing Cover
S|CA HT SB QC:DIM TR QC:VT ML DR QC:DIM+UT TR
P|16.13.01.005.2|G.O.V Housing End UH
S|CA HT SB QC:DIM TR QC:VT ML QC:UT DR QC:DIM+UT TR
P|16.11.01.010.2|Gland case LH
S|PO CT WE HT SB QC:DIM TR QC:VT ML DR QC:DIM+UT TR
P|16.13.01.000.2|Casing
S|AS FM QC:DIM HY QC:DIM TR ST
R|16.13.04.000.2|Journal Bearing|CO-00
P|16.13.04.000.2|Journal Bearing
S|PO CV QC:DIM RG QC:DIM WC JM DR FM QC:DIM NT:PT QC:DIM PK TR ST
R|16.11.01.012.2|Carbon ring|CO-00
P|16.11.01.012.2|Carbon Packing ring
S|CV QC:DIM CT MC QC:DIM AS QC:DIM TR ST
P|16.11.01.012.2.S2|Carbon Packing ring spring
S|PO FO QC:DIM
P|16.11.01.012.2.S1|Packing ring spring stop
S|PO MC QC:DIM
R|16.94.04.000.2|Valve 2 inch|CO-00
P|16.94.04.001.2|Governor Valve body 600#
S|CA SB QC:DIM HT TH:TENSILE+HARDNESS TR RG QC:DIM TR FM QC:DIM+NDT TR PK ST
P|16.94.04.007.2|Governor Valve bonnet
S|CA SB QC:DIM HT TR RG QC:DIM TR FM QC:DIM+NDT TR PK ST
P|16.94.04.002.2|Valve cage
S|PO CV QC:DIM SP CT RL HT QC:DIM TR FM GR NI QC:DIM PK ST
P|16.94.04.000.2|Valve 2 inch
S|AS SD SA QC:DIM PK ST
R|16.13.02.000.2|Rotor|CO-00
P|16.11.02.001.2|Shaft
S|PO CV QC:DIM SP TR CT RL QC:UT TR HT TH:TENSILE+HARDNESS+IMPACT TR RL FL QC:DIM TR ML GR QC:UT+MT TR ST
P|16.11.02.002.2|Wheel
S|PO CV QC:DIM SP TR RL QC:UT TR HT TH:TENSILE+HARDNESS TR FL ML WC GR QC:DIM QC:UT+MT TR ST
P|16.13.02.211.2|Governor Disk
S|PO CV QC:DIM SP TR LA QC:UT HT FM QC:DIM TR ST
P|16.13.02.000.2|Rotor
S|AS SA BL QC:RUNOUT+DIM TR ST
R|16.13.03.000.2|Hand Valve|CO-00
P|16.13.03.001.2|Hand valve Body
S|CA HT SB QC:DIM TR RG QC:UT+MT TR FM QC:DIM ST
P|16.13.03.002.2|Hand Valve Stem
S|PO CV QC:DIM SP CT RL HT QC:HARDNESS TR FM GR NI QC:DIM PK ST
P|16.13.03.003.2|Hand Valve Plunger
S|PO CV QC:DIM CT RL HT QC:HARDNESS FM QC:DIM PK ST
P|16.13.03.000.2|Hand Valve
S|AS SD QC:DIM PK ST
R|16.13.05.000.2|Nozzle and Reversing chamber|CO-00
P|16.13.05.003.2|Jet Body Holder
S|PO CV CT QC:DIM MC QC:DIM ST
P|16.13.05.002.2|Steamjet Body
S|CA HT SB QC:DIM MC QC:DIM ST
P|16.13.09.101.2|Reversing chamber
S|CA HT SB QC:DIM MC QC:DIM ST
""";
        public const string Wbs = """
0|FY Platform|108.5|0.0|0
1|Start|0.0|0.0|1
1|Make Or Buy Analsis|74.5|0.0|0
2|Casing|74.5|0.0|0
3|BOM|14.0|0.0|1
3|Wheel Case|73.0|0.0|0
4|E|6.0|0.0|0
4|P|35.5|15.167|0
4|C|23.5|55.208|0
3|Casing Cover|74.5|0.0|0
4|E|6.0|0.0|0
4|P|36.0|15.167|0
4|C|24.5|56.0|0
3|G.O.V Housing End LH|60.0|0.0|0
4|E|6.0|0.0|0
4|P|33.5|15.167|0
4|C|6.0|60.292|0
3|G.O.V Housing End LH|61.0|0.0|0
4|E|6.0|0.0|0
4|P|34.0|15.167|0
4|C|5.0|62.292|0
3|Bearing Cap|51.0|0.0|0
4|E|6.0|0.0|0
4|P|33.0|15.167|0
4|C|4.0|53.042|0
3|Governor housing  FY|51.0|0.0|0
4|E|6.0|0.0|0
4|P|33.0|15.167|0
4|C|4.0|53.042|0
3|STD Parts for Casing|15.0|15.208|0
4|E|3.0|15.208|0
4|P|12.0|19.083|0
2|Beaings|46.0|0.0|0
3|BOM|14.0|0.0|1
3|Coupling End Bearing|46.0|0.0|0
4|E|6.0|0.0|0
4|P|32.0|15.167|0
3|Governor End Bearing|28.25|0.0|0
4|E|6.0|0.0|0
4|P|14.25|15.167|0
3|Oil Ring FY|20.125|0.0|0
4|E|6.0|0.0|0
4|P|5.0625|15.208|0
4|C|1.0625|21.104|0
3|STD Parts for Bearing|15.0|15.208|0
4|E|3.0|15.208|0
4|P|12.0|19.083|0
2|Carbon Ring|10.0|0.0|0
3|BOM|7.0|0.0|1
3|Carbon Ring Assembly|10.0|0.0|0
4|E|6.0|0.0|0
4|P|3.0|7.292|0
2|Gland Case|45.0|0.0|0
3|BOM|7.0|0.0|1
3|Gland Case LH|41.5|0.0|0
4|E|6.0|0.0|0
4|P|32.0|7.292|0
4|C|2.5|43.208|0
3|Gland Case UH|41.5|0.0|0
4|E|6.0|0.0|0
4|P|32.0|7.292|0
4|C|2.5|43.208|0
3|Gland Case Assembly|3.5|46.292|0
4|Assemble Gland Case LH/UH|0.5|46.292|1
4|Blue Check|0.5|47.083|1
4|QC (Check & Report)|0.5|47.292|1
4|Lathing|1.0|48.083|1
4|QC(VT,DIM)|1.0|49.083|1
4|Inventory Receipt|0.0|50.083|1
3|STD Parts for Gland Case|24.03125|7.292|0
4|E|3.0|7.292|0
4|P|21.03125|11.208|0
2|Gov. Disk|22.0|0.0|0
3|Bom|7.0|0.0|1
3|Governor Disk|14.0|0.0|0
4|E|6.0|0.0|0
4|P|3.0|7.292|0
4|C|4.0|11.208|0
3|Emerg. Weight Screw|13.875|0.0|0
4|E|6.0|0.0|0
4|P|5.0|7.292|0
4|C|1.875|13.208|0
3|Emerg Spring adj stud|12.0625|0.0|0
4|E|2.0|0.0|0
4|P|2.0625|7.292|0
4|C|3.0|10.271|0
3|Emergency Spring Seat|13.5|0.0|0
4|E|6.0|0.0|0
4|P|5.0|7.292|0
4|C|1.5|13.208|0
3|Emerg Gov weight|13.5|0.0|0
4|E|2.0|0.0|0
4|P|5.0|7.292|0
4|C|1.5|13.208|0
3|Emergency Weight Stop Pin|13.5|0.0|0
4|E|6.0|0.0|0
4|P|5.0|7.292|0
4|C|1.5|13.208|0
3|Emerg Weight spring|11.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|7.292|0
3|STD Parts for Gov. Disk|15.0|7.292|0
4|E|3.0|7.292|0
4|P|12.0|11.208|0
2|Hand Valve|44.5625|0.0|0
3|BOM|14.0|0.0|1
3|Hand Valve Body|40.1875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|2.125|42.229|0
3|Hand Valve Stem|44.5625|0.0|0
4|E|6.0|0.0|0
4|P|8.0625|15.167|0
4|C|2.5|47.104|0
3|Hand Valve Plunger|41.5|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|3.4375|42.229|0
3|Hand valve Packing Follower|39.5625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.5|42.229|0
3|Hand valve Packing nut|39.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.0|42.229|0
3|Hand Valve Graphite Packing|18.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
3|Hand Valve Plunger Pin|39.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.0|42.229|0
3|Plug&Hand Valve gasket|18.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
3|STD Parts for Hand Valve|15.0|15.208|0
4|E|3.0|15.208|0
4|P|12.0|19.083|0
2|Nozzle|35.6875|0.0|0
3|BOM|14.0|0.0|1
3|Steam Jet|35.6875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|7.625|31.354|0
3|Steam Jet Body|35.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|7.0|31.354|0
3|Jet Body Holder|35.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|7.0|31.354|0
3|Steam Jet Gasket|18.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
3|Jet Body Gasket|18.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
3|STD Parts for Nozzle|26.0|0.0|0
4|E|3.0|0.0|0
4|P|12.0|15.167|0
2|Reversing chamber|52.0|0.0|0
3|BOM|14.0|0.0|1
3|Reversing chamber|52.0|0.0|0
4|E|6.0|0.0|0
4|P|32.0|15.167|0
4|C|6.0|52.083|0
3|STD Parts for Reversing Chamber|26.0|0.0|0
4|E|3.0|0.0|0
4|P|12.0|15.167|0
2|Rotor|41.0|0.0|0
3|BOM|14.0|0.0|1
3|Shaft|35.5625|0.0|0
4|E|6.0|0.0|0
4|P|24.0625|0.0|0
4|C|11.5|27.021|0
3|Wheel|41.0|0.0|0
4|E|6.0|0.0|0
4|P|15.0|15.167|0
4|C|12.0|32.292|0
3|Wheel shaft nut|29.1875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|11.125|20.104|0
3|Steam Bunter Collar Small|19.25|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.1875|20.104|0
3|Steam Bunter Collar Large|19.25|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.1875|20.104|0
3|Oil deflector|19.6875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.125|20.312|0
3|Gov spring adj nut|19.1875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.125|20.104|0
3|Copper Piece|18.875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|0.3125|20.312|0
3|Ball Bearing Spacer|18.875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|0.3125|20.312|0
3|STD Parts for Rotor|26.0|0.0|0
4|E|3.0|0.0|0
4|P|12.0|15.167|0
2|Valve|61.5|0.0|0
3|BOM|14.0|0.0|1
3|Governor Valve body|55.0|0.0|0
4|E|6.0|0.0|0
4|P|32.0|15.167|0
4|C|9.0|52.083|0
3|Valve cage|34.8125|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|16.0|21.021|0
3|Governor valve|26.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|6.0|22.104|0
3|Valve stem head|30.53125|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|2.46875|31.354|0
3|Valve stem|23.3125|0.0|0
4|E|2.0|0.0|0
4|P|8.3125|15.167|0
4|C|1.0|25.146|0
3|Bonnet Stuffing Box Body|23.8125|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|3.75|22.104|0
3|Governor valve bonnet|61.5|0.0|0
4|E|6.0|0.0|0
4|P|32.0|15.167|0
4|C|15.5|52.083|0
3|Bonnet Stuff Box Follower|23.6875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|0.625|26.021|0
3|Stainer|19.1875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.125|20.104|0
3|Bonnet Stuff Box Nut|24.3125|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|3.75|22.312|0
3|Bonnet Stuffing Box Copper washer|18.125|0.0|0
4|E|6.0|0.0|0
4|P|4.125|15.167|0
3|Valve Stem Guide|22.0|0.0|0
4|E|2.0|0.0|0
4|P|4.0|15.208|0
4|C|4.0|20.083|0
3|Butterfly Valve stem|30.625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|2.5625|31.354|0
3|Butterfly Vavle|27.0|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|7.4375|21.312|0
3|Emergency Resetting lever|18.6875|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|0.625|20.104|0
3|Emergency Valve Spring|18.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
3|Back lash spring|18.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
3|Resting Lever Washer|18.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
3|Emergency Trip Bracket|22.75|0.0|0
4|E|6.0|0.0|0
4|P|5.0|15.167|0
4|C|3.75|21.083|0
3|Emergency Spindle Cam|19.75|0.0|0
4|E|6.0|0.0|0
4|P|5.0|15.167|0
4|C|0.625|21.125|0
3|Emergency valve packing follower|21.9375|0.0|0
4|E|6.0|0.0|0
4|P|5.0|15.167|0
4|C|1.125|24.021|0
3|Emergency valve stuffing box body|22.75|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.9375|24.021|0
3|Emergency valve packing nut|23.5625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|2.75|24.021|0
3|Valve spring seat lower|21.9375|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
4|C|1.125|24.021|0
3|Bonnet graphit packing ring|18.0625|0.0|0
4|E|6.0|0.0|0
4|P|4.0625|15.167|0
3|STD Parts for Valve|27.0|0.0|0
4|E|3.0|0.0|0
4|P|13.0|15.167|0
1|Assemble|63.5|46.125|0
2|HP Hydrotest|0.0|78.292|1
2|QC(Record and Archive Final Book Documents)|0.0|78.292|1
2|LP Hydrotest|0.0|82.167|1
2|QC(Record and Archive Final Book Documents)|0.0|82.167|1
2|Blue Check - Casing Cover to Wheel Case|1.0|83.208|1
2|QC(Record and Archive in internal Final Book Documents)|0.0|84.167|1
2|Blue Check - Bearing Cap to Wheel Case|0.5|84.208|1
2|QC|0.0|84.375|1
2|Blue Check - Gov Housing To Wheel Case|0.5|85.0|1
2|QC|0.0|85.167|1
2|Shaft Shirinked to Wheel|2.0|46.125|1
2|Rotor Fully Assemble|1.0|48.083|1
2|Rotor Balance and Run out Test|3.0|49.083|1
2|Gov Valve Body HP Hydrotest|1.0|61.292|1
2|QC(Record and Archive in internal Final Book Documents)|0.0|62.292|1
2|Alignments - Rotor and Wheel Case|2.0|85.208|1
2|Painting|7.0|88.125|1
2|QC|0.0|95.375|1
2|Reversing Chamber Assembly|2.0|96.0|1
2|Nozzles Assembly|2.0|98.0|1
2|Bearings|2.0|101.0|1
2|Gland Asembly|1.0|102.292|1
2|Alignments|3.0|103.292|1
2|Clearance|2.0|106.292|1
2|Carbon Ring Assembly|1.0|109.208|1
2|Cover Full Assemble|2.0|110.208|1
2|Valve Assemble|3.0|62.292|1
2|Valve Assemble To Wheel Case|1.0|112.208|1
2|Hand Valve Assemble|1.0|113.208|1
2|Sentinel Valve Assembly|0.5|112.208|1
2|RunTest|1.0|115.167|1
2|QC(Record and Archive Final Book Documents)|0.0|116.125|1
2|Inspection Notifications|0.0|116.125|1
2|RunTest|1.0|116.125|1
1|Packing|3.0|117.083|1
1|Shipping|1.0|120.083|1
1|Finish|0.0|122.083|1
""";
        public const string Legacy = """
02101001000010|Wheel case -قطعه خام ریخته گری|-
02101002000110|casing cover-قطعه خام ریخته گری|-
02101004000010|Bearing cap-قطعه خام ریخته گری|-
02160001030410|Gov. valve body-قطعه خام ریخته گری|-
02160005030010|Governor Valve bonnet-قطعه خام ریخته گری|-
02220001000010|Gov housing-قطعه خام ریخته گری|-
02130004000014|Steam bunter collar small-فرز(milling)|-
02130007000014|Steam bunter collar large-فرز(milling)|-
02162064020019|Actuator Flange|-
02220000000003|Gov. end bearing LH / UH ASSY. FY|-
02725349520003|one side threaded NPT 1 1/2" nipple- Sch-40S-length 3"-SS316L-قطعه نهایی|SS316L
02725316000003|Short Pipe Nipple 1/2" Sch. 40 Tow Side Threaded-SS316-150mm-قطعه نهایی|SS316
01005253080101|Pipe Sch40-SS316-4inch|SS 316
01005274060101|Pipe OD120 ID100|SS 304
01005254080101|Pipe Sch40-SS316-2inch|SS 316
01001021090101|round bar-dia80|SS 420
01001025090101|round bar-dia100|SS 420
01001714060101|round bar -dia85|SS 304
01003271060101|ورق-SS 304-sheet 1000*500*10|SS 304
01006073080101|چهارپر-SS 316-square bar -dia 40*40|SS 316
01001037010101|میلگرد-MO40-round bar-dia140|MO40
01001723120000|میلگرد-CK45-round bar -dia150|CK45
08956063870011|Flange 3/4" Socket Weld #600 SCH80|A105
08956064860011|Flange 3/4" Socket Weld #300 SCH80|A105N
08956062870011|Flange 1" Slip-on #150|A105
08956006230011|Flange 1" Weldneck #150 SCH40s|Stainless Steel 304L
08956019850011|Flange 3/4" Weldneck #150 SCH80s|Stainless Steel 316
12006003330201|Temperature Transmitter-YOKOGAWA-YTA-110EA4DB/SU2|Aluminium
12006001310101|Pressure Transmitter Yokogawa-EJX530A-JBS7N-014DN/KU22|Stainless Steel
12006002320101|Pressure Differential Indicator Transmitter Yokogawa-EJA110A-EHS0B-24DN/KU22|Stainless Steel
12006005871501|DPT/ Level Transmitter  Rosemount-3051 CD1A22A1AS5B4I1M5Q4|-
06005962000000|چسب لاکتایت 270|-
06005961000000|چسب لاکتایت 638|-
06005910000000|چسب لاکتایت 603|-
06005106000000|تیوب بلوچک|-
05016086000000|آچار تخت رینگی چکش خور 50|-
05010174010000|آچار رینگی چکش خور 34 میلیمتر|-
05016009000000|آچار تخت 24|-
05010035000000|آچار مهره ER40|-
""";
    }
}
