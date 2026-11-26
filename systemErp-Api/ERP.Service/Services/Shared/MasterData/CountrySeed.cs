namespace ERP.Service.Services.Shared;

/// <summary>
/// دول العالم (ISO 3166-1) لتعبئة قائمة دول المنشأة أول مرة؛ بعدها تُدار من شاشة الدول.
/// السطر: الرمز الثنائي | الثلاثي | الاسم العربي | الاسم الإنجليزي | مفتاح الاتصال | العملة | الجنسية بالعربية | بالإنجليزية.
/// </summary>
internal static class CountrySeed
{
    public sealed record Row(string Code, string Code3, string NameAr, string NameEn, string DialCode, string Currency, string NationalityAr, string NationalityEn);

    public static IReadOnlyList<Row> All { get; } = Parse(Data);

    private static List<Row> Parse(string data)
        => data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|'))
            .Where(p => p.Length == 8)
            .Select(p => new Row(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]))
            .ToList();

    private const string Data = """
        SA|SAU|السعودية|Saudi Arabia|+966|SAR|سعودي|Saudi
        AE|ARE|الإمارات|United Arab Emirates|+971|AED|إماراتي|Emirati
        KW|KWT|الكويت|Kuwait|+965|KWD|كويتي|Kuwaiti
        BH|BHR|البحرين|Bahrain|+973|BHD|بحريني|Bahraini
        QA|QAT|قطر|Qatar|+974|QAR|قطري|Qatari
        OM|OMN|عُمان|Oman|+968|OMR|عُماني|Omani
        YE|YEM|اليمن|Yemen|+967|YER|يمني|Yemeni
        IQ|IRQ|العراق|Iraq|+964|IQD|عراقي|Iraqi
        JO|JOR|الأردن|Jordan|+962|JOD|أردني|Jordanian
        SY|SYR|سوريا|Syria|+963|SYP|سوري|Syrian
        LB|LBN|لبنان|Lebanon|+961|LBP|لبناني|Lebanese
        PS|PSE|فلسطين|Palestine|+970|ILS|فلسطيني|Palestinian
        EG|EGY|مصر|Egypt|+20|EGP|مصري|Egyptian
        SD|SDN|السودان|Sudan|+249|SDG|سوداني|Sudanese
        SS|SSD|جنوب السودان|South Sudan|+211|SSP|جنوب سوداني|South Sudanese
        LY|LBY|ليبيا|Libya|+218|LYD|ليبي|Libyan
        TN|TUN|تونس|Tunisia|+216|TND|تونسي|Tunisian
        DZ|DZA|الجزائر|Algeria|+213|DZD|جزائري|Algerian
        MA|MAR|المغرب|Morocco|+212|MAD|مغربي|Moroccan
        MR|MRT|موريتانيا|Mauritania|+222|MRU|موريتاني|Mauritanian
        SO|SOM|الصومال|Somalia|+252|SOS|صومالي|Somali
        DJ|DJI|جيبوتي|Djibouti|+253|DJF|جيبوتي|Djiboutian
        KM|COM|جزر القمر|Comoros|+269|KMF|قمري|Comorian
        TR|TUR|تركيا|Turkey|+90|TRY|تركي|Turkish
        IR|IRN|إيران|Iran|+98|IRR|إيراني|Iranian
        AF|AFG|أفغانستان|Afghanistan|+93|AFN|أفغاني|Afghan
        PK|PAK|باكستان|Pakistan|+92|PKR|باكستاني|Pakistani
        IN|IND|الهند|India|+91|INR|هندي|Indian
        BD|BGD|بنغلاديش|Bangladesh|+880|BDT|بنغلاديشي|Bangladeshi
        LK|LKA|سريلانكا|Sri Lanka|+94|LKR|سريلانكي|Sri Lankan
        NP|NPL|نيبال|Nepal|+977|NPR|نيبالي|Nepali
        BT|BTN|بوتان|Bhutan|+975|BTN|بوتاني|Bhutanese
        MV|MDV|جزر المالديف|Maldives|+960|MVR|مالديفي|Maldivian
        PH|PHL|الفلبين|Philippines|+63|PHP|فلبيني|Filipino
        ID|IDN|إندونيسيا|Indonesia|+62|IDR|إندونيسي|Indonesian
        MY|MYS|ماليزيا|Malaysia|+60|MYR|ماليزي|Malaysian
        SG|SGP|سنغافورة|Singapore|+65|SGD|سنغافوري|Singaporean
        TH|THA|تايلاند|Thailand|+66|THB|تايلاندي|Thai
        VN|VNM|فيتنام|Vietnam|+84|VND|فيتنامي|Vietnamese
        KH|KHM|كمبوديا|Cambodia|+855|KHR|كمبودي|Cambodian
        LA|LAO|لاوس|Laos|+856|LAK|لاوسي|Lao
        MM|MMR|ميانمار|Myanmar|+95|MMK|ميانماري|Burmese
        BN|BRN|بروناي|Brunei|+673|BND|بروناوي|Bruneian
        TL|TLS|تيمور الشرقية|Timor-Leste|+670|USD|تيموري|Timorese
        CN|CHN|الصين|China|+86|CNY|صيني|Chinese
        HK|HKG|هونغ كونغ|Hong Kong|+852|HKD|هونغ كونغي|Hong Konger
        MO|MAC|ماكاو|Macao|+853|MOP|ماكاوي|Macanese
        TW|TWN|تايوان|Taiwan|+886|TWD|تايواني|Taiwanese
        JP|JPN|اليابان|Japan|+81|JPY|ياباني|Japanese
        KR|KOR|كوريا الجنوبية|South Korea|+82|KRW|كوري جنوبي|South Korean
        KP|PRK|كوريا الشمالية|North Korea|+850|KPW|كوري شمالي|North Korean
        MN|MNG|منغوليا|Mongolia|+976|MNT|منغولي|Mongolian
        KZ|KAZ|كازاخستان|Kazakhstan|+7|KZT|كازاخستاني|Kazakh
        UZ|UZB|أوزبكستان|Uzbekistan|+998|UZS|أوزبكي|Uzbek
        TM|TKM|تركمانستان|Turkmenistan|+993|TMT|تركماني|Turkmen
        KG|KGZ|قيرغيزستان|Kyrgyzstan|+996|KGS|قيرغيزي|Kyrgyz
        TJ|TJK|طاجيكستان|Tajikistan|+992|TJS|طاجيكي|Tajik
        AZ|AZE|أذربيجان|Azerbaijan|+994|AZN|أذربيجاني|Azerbaijani
        AM|ARM|أرمينيا|Armenia|+374|AMD|أرميني|Armenian
        GE|GEO|جورجيا|Georgia|+995|GEL|جورجي|Georgian
        CY|CYP|قبرص|Cyprus|+357|EUR|قبرصي|Cypriot
        IL|ISR|إسرائيل|Israel|+972|ILS|إسرائيلي|Israeli
        GB|GBR|المملكة المتحدة|United Kingdom|+44|GBP|بريطاني|British
        IE|IRL|أيرلندا|Ireland|+353|EUR|أيرلندي|Irish
        FR|FRA|فرنسا|France|+33|EUR|فرنسي|French
        DE|DEU|ألمانيا|Germany|+49|EUR|ألماني|German
        IT|ITA|إيطاليا|Italy|+39|EUR|إيطالي|Italian
        ES|ESP|إسبانيا|Spain|+34|EUR|إسباني|Spanish
        PT|PRT|البرتغال|Portugal|+351|EUR|برتغالي|Portuguese
        NL|NLD|هولندا|Netherlands|+31|EUR|هولندي|Dutch
        BE|BEL|بلجيكا|Belgium|+32|EUR|بلجيكي|Belgian
        LU|LUX|لوكسمبورغ|Luxembourg|+352|EUR|لوكسمبورغي|Luxembourgish
        CH|CHE|سويسرا|Switzerland|+41|CHF|سويسري|Swiss
        AT|AUT|النمسا|Austria|+43|EUR|نمساوي|Austrian
        LI|LIE|ليختنشتاين|Liechtenstein|+423|CHF|ليختنشتايني|Liechtensteiner
        MC|MCO|موناكو|Monaco|+377|EUR|موناكي|Monegasque
        AD|AND|أندورا|Andorra|+376|EUR|أندوري|Andorran
        SM|SMR|سان مارينو|San Marino|+378|EUR|سان ماريني|Sammarinese
        VA|VAT|الفاتيكان|Vatican City|+379|EUR|فاتيكاني|Vatican
        MT|MLT|مالطا|Malta|+356|EUR|مالطي|Maltese
        GR|GRC|اليونان|Greece|+30|EUR|يوناني|Greek
        SE|SWE|السويد|Sweden|+46|SEK|سويدي|Swedish
        NO|NOR|النرويج|Norway|+47|NOK|نرويجي|Norwegian
        DK|DNK|الدنمارك|Denmark|+45|DKK|دنماركي|Danish
        FI|FIN|فنلندا|Finland|+358|EUR|فنلندي|Finnish
        IS|ISL|آيسلندا|Iceland|+354|ISK|آيسلندي|Icelandic
        PL|POL|بولندا|Poland|+48|PLN|بولندي|Polish
        CZ|CZE|التشيك|Czechia|+420|CZK|تشيكي|Czech
        SK|SVK|سلوفاكيا|Slovakia|+421|EUR|سلوفاكي|Slovak
        HU|HUN|المجر|Hungary|+36|HUF|مجري|Hungarian
        RO|ROU|رومانيا|Romania|+40|RON|روماني|Romanian
        BG|BGR|بلغاريا|Bulgaria|+359|BGN|بلغاري|Bulgarian
        RS|SRB|صربيا|Serbia|+381|RSD|صربي|Serbian
        HR|HRV|كرواتيا|Croatia|+385|EUR|كرواتي|Croatian
        SI|SVN|سلوفينيا|Slovenia|+386|EUR|سلوفيني|Slovenian
        BA|BIH|البوسنة والهرسك|Bosnia and Herzegovina|+387|BAM|بوسني|Bosnian
        ME|MNE|الجبل الأسود|Montenegro|+382|EUR|مونتينيغري|Montenegrin
        MK|MKD|مقدونيا الشمالية|North Macedonia|+389|MKD|مقدوني|Macedonian
        AL|ALB|ألبانيا|Albania|+355|ALL|ألباني|Albanian
        XK|XKX|كوسوفو|Kosovo|+383|EUR|كوسوفي|Kosovar
        EE|EST|إستونيا|Estonia|+372|EUR|إستوني|Estonian
        LV|LVA|لاتفيا|Latvia|+371|EUR|لاتفي|Latvian
        LT|LTU|ليتوانيا|Lithuania|+370|EUR|ليتواني|Lithuanian
        BY|BLR|بيلاروسيا|Belarus|+375|BYN|بيلاروسي|Belarusian
        UA|UKR|أوكرانيا|Ukraine|+380|UAH|أوكراني|Ukrainian
        MD|MDA|مولدوفا|Moldova|+373|MDL|مولدوفي|Moldovan
        RU|RUS|روسيا|Russia|+7|RUB|روسي|Russian
        US|USA|الولايات المتحدة|United States|+1|USD|أمريكي|American
        CA|CAN|كندا|Canada|+1|CAD|كندي|Canadian
        MX|MEX|المكسيك|Mexico|+52|MXN|مكسيكي|Mexican
        GT|GTM|غواتيمالا|Guatemala|+502|GTQ|غواتيمالي|Guatemalan
        BZ|BLZ|بليز|Belize|+501|BZD|بليزي|Belizean
        SV|SLV|السلفادور|El Salvador|+503|USD|سلفادوري|Salvadoran
        HN|HND|هندوراس|Honduras|+504|HNL|هندوراسي|Honduran
        NI|NIC|نيكاراغوا|Nicaragua|+505|NIO|نيكاراغوي|Nicaraguan
        CR|CRI|كوستاريكا|Costa Rica|+506|CRC|كوستاريكي|Costa Rican
        PA|PAN|بنما|Panama|+507|PAB|بنمي|Panamanian
        CU|CUB|كوبا|Cuba|+53|CUP|كوبي|Cuban
        JM|JAM|جامايكا|Jamaica|+1-876|JMD|جامايكي|Jamaican
        HT|HTI|هايتي|Haiti|+509|HTG|هايتي|Haitian
        DO|DOM|جمهورية الدومينيكان|Dominican Republic|+1-809|DOP|دومينيكاني|Dominican
        PR|PRI|بورتوريكو|Puerto Rico|+1-787|USD|بورتوريكي|Puerto Rican
        BS|BHS|جزر البهاما|Bahamas|+1-242|BSD|بهامي|Bahamian
        BB|BRB|باربادوس|Barbados|+1-246|BBD|بربادوسي|Barbadian
        TT|TTO|ترينيداد وتوباغو|Trinidad and Tobago|+1-868|TTD|ترينيدادي|Trinidadian
        AG|ATG|أنتيغوا وباربودا|Antigua and Barbuda|+1-268|XCD|أنتيغوي|Antiguan
        DM|DMA|دومينيكا|Dominica|+1-767|XCD|دومينيكي|Dominican
        GD|GRD|غرينادا|Grenada|+1-473|XCD|غرينادي|Grenadian
        KN|KNA|سانت كيتس ونيفيس|Saint Kitts and Nevis|+1-869|XCD|كيتسي|Kittitian
        LC|LCA|سانت لوسيا|Saint Lucia|+1-758|XCD|لوسي|Saint Lucian
        VC|VCT|سانت فنسنت والغرينادين|Saint Vincent and the Grenadines|+1-784|XCD|فنسنتي|Vincentian
        BR|BRA|البرازيل|Brazil|+55|BRL|برازيلي|Brazilian
        AR|ARG|الأرجنتين|Argentina|+54|ARS|أرجنتيني|Argentine
        CL|CHL|تشيلي|Chile|+56|CLP|تشيلي|Chilean
        CO|COL|كولومبيا|Colombia|+57|COP|كولومبي|Colombian
        PE|PER|بيرو|Peru|+51|PEN|بيروفي|Peruvian
        VE|VEN|فنزويلا|Venezuela|+58|VES|فنزويلي|Venezuelan
        EC|ECU|الإكوادور|Ecuador|+593|USD|إكوادوري|Ecuadorian
        BO|BOL|بوليفيا|Bolivia|+591|BOB|بوليفي|Bolivian
        PY|PRY|باراغواي|Paraguay|+595|PYG|باراغواياني|Paraguayan
        UY|URY|الأوروغواي|Uruguay|+598|UYU|أوروغواياني|Uruguayan
        GY|GUY|غيانا|Guyana|+592|GYD|غياني|Guyanese
        SR|SUR|سورينام|Suriname|+597|SRD|سورينامي|Surinamese
        AU|AUS|أستراليا|Australia|+61|AUD|أسترالي|Australian
        NZ|NZL|نيوزيلندا|New Zealand|+64|NZD|نيوزيلندي|New Zealander
        PG|PNG|بابوا غينيا الجديدة|Papua New Guinea|+675|PGK|بابوي|Papua New Guinean
        FJ|FJI|فيجي|Fiji|+679|FJD|فيجي|Fijian
        SB|SLB|جزر سليمان|Solomon Islands|+677|SBD|سليماني|Solomon Islander
        VU|VUT|فانواتو|Vanuatu|+678|VUV|فانواتي|Ni-Vanuatu
        WS|WSM|ساموا|Samoa|+685|WST|ساموي|Samoan
        TO|TON|تونغا|Tonga|+676|TOP|تونغي|Tongan
        KI|KIR|كيريباتي|Kiribati|+686|AUD|كيريباتي|I-Kiribati
        TV|TUV|توفالو|Tuvalu|+688|AUD|توفالي|Tuvaluan
        NR|NRU|ناورو|Nauru|+674|AUD|ناوروي|Nauruan
        FM|FSM|ميكرونيزيا|Micronesia|+691|USD|ميكرونيزي|Micronesian
        MH|MHL|جزر مارشال|Marshall Islands|+692|USD|مارشالي|Marshallese
        PW|PLW|بالاو|Palau|+680|USD|بالاوي|Palauan
        NG|NGA|نيجيريا|Nigeria|+234|NGN|نيجيري|Nigerian
        GH|GHA|غانا|Ghana|+233|GHS|غاني|Ghanaian
        CI|CIV|ساحل العاج|Côte d'Ivoire|+225|XOF|إيفواري|Ivorian
        SN|SEN|السنغال|Senegal|+221|XOF|سنغالي|Senegalese
        ML|MLI|مالي|Mali|+223|XOF|مالي|Malian
        BF|BFA|بوركينا فاسو|Burkina Faso|+226|XOF|بوركيني|Burkinabe
        NE|NER|النيجر|Niger|+227|XOF|نيجري|Nigerien
        TD|TCD|تشاد|Chad|+235|XAF|تشادي|Chadian
        CM|CMR|الكاميرون|Cameroon|+237|XAF|كاميروني|Cameroonian
        CF|CAF|جمهورية أفريقيا الوسطى|Central African Republic|+236|XAF|أفريقي أوسطي|Central African
        GA|GAB|الغابون|Gabon|+241|XAF|غابوني|Gabonese
        CG|COG|الكونغو|Congo|+242|XAF|كونغولي|Congolese
        CD|COD|الكونغو الديمقراطية|DR Congo|+243|CDF|كونغولي|Congolese
        GQ|GNQ|غينيا الاستوائية|Equatorial Guinea|+240|XAF|غيني استوائي|Equatoguinean
        ST|STP|ساو تومي وبرينسيب|Sao Tome and Principe|+239|STN|ساو تومي|Sao Tomean
        AO|AGO|أنغولا|Angola|+244|AOA|أنغولي|Angolan
        ET|ETH|إثيوبيا|Ethiopia|+251|ETB|إثيوبي|Ethiopian
        ER|ERI|إريتريا|Eritrea|+291|ERN|إريتري|Eritrean
        KE|KEN|كينيا|Kenya|+254|KES|كيني|Kenyan
        UG|UGA|أوغندا|Uganda|+256|UGX|أوغندي|Ugandan
        TZ|TZA|تنزانيا|Tanzania|+255|TZS|تنزاني|Tanzanian
        RW|RWA|رواندا|Rwanda|+250|RWF|رواندي|Rwandan
        BI|BDI|بوروندي|Burundi|+257|BIF|بوروندي|Burundian
        MZ|MOZ|موزمبيق|Mozambique|+258|MZN|موزمبيقي|Mozambican
        MW|MWI|مالاوي|Malawi|+265|MWK|مالاوي|Malawian
        ZM|ZMB|زامبيا|Zambia|+260|ZMW|زامبي|Zambian
        ZW|ZWE|زيمبابوي|Zimbabwe|+263|ZWL|زيمبابوي|Zimbabwean
        BW|BWA|بوتسوانا|Botswana|+267|BWP|بوتسواني|Motswana
        NA|NAM|ناميبيا|Namibia|+264|NAD|ناميبي|Namibian
        ZA|ZAF|جنوب أفريقيا|South Africa|+27|ZAR|جنوب أفريقي|South African
        LS|LSO|ليسوتو|Lesotho|+266|LSL|ليسوتي|Mosotho
        SZ|SWZ|إسواتيني|Eswatini|+268|SZL|سوازيلاندي|Swazi
        MG|MDG|مدغشقر|Madagascar|+261|MGA|مدغشقري|Malagasy
        MU|MUS|موريشيوس|Mauritius|+230|MUR|موريشيوسي|Mauritian
        SC|SYC|سيشل|Seychelles|+248|SCR|سيشلي|Seychellois
        CV|CPV|الرأس الأخضر|Cabo Verde|+238|CVE|رأس أخضري|Cabo Verdean
        GM|GMB|غامبيا|Gambia|+220|GMD|غامبي|Gambian
        GN|GIN|غينيا|Guinea|+224|GNF|غيني|Guinean
        GW|GNB|غينيا بيساو|Guinea-Bissau|+245|XOF|غيني بيساوي|Bissau-Guinean
        SL|SLE|سيراليون|Sierra Leone|+232|SLE|سيراليوني|Sierra Leonean
        LR|LBR|ليبيريا|Liberia|+231|LRD|ليبيري|Liberian
        TG|TGO|توغو|Togo|+228|XOF|توغولي|Togolese
        BJ|BEN|بنين|Benin|+229|XOF|بنيني|Beninese
        EH|ESH|الصحراء الغربية|Western Sahara|+212|MAD|صحراوي|Sahrawi
        GL|GRL|جرينلاند|Greenland|+299|DKK|جرينلاندي|Greenlandic
        FO|FRO|جزر فارو|Faroe Islands|+298|DKK|فاروي|Faroese
        GI|GIB|جبل طارق|Gibraltar|+350|GIP|جبل طارقي|Gibraltarian
        IM|IMN|جزيرة مان|Isle of Man|+44|GBP|مانكسي|Manx
        JE|JEY|جيرزي|Jersey|+44|GBP|جيرزي|Jersey
        GG|GGY|غيرنزي|Guernsey|+44|GBP|غيرنزي|Guernsey
        AX|ALA|جزر أولاند|Åland Islands|+358|EUR|أولاندي|Ålandish
        SJ|SJM|سفالبارد ويان ماين|Svalbard and Jan Mayen|+47|NOK|نرويجي|Norwegian
        BM|BMU|برمودا|Bermuda|+1-441|BMD|برمودي|Bermudian
        KY|CYM|جزر كايمان|Cayman Islands|+1-345|KYD|كايماني|Caymanian
        VG|VGB|جزر العذراء البريطانية|British Virgin Islands|+1-284|USD|عذراوي بريطاني|Virgin Islander
        VI|VIR|جزر العذراء الأمريكية|U.S. Virgin Islands|+1-340|USD|عذراوي أمريكي|Virgin Islander
        AI|AIA|أنغويلا|Anguilla|+1-264|XCD|أنغويلي|Anguillian
        MS|MSR|مونتسرات|Montserrat|+1-664|XCD|مونتسراتي|Montserratian
        TC|TCA|جزر توركس وكايكوس|Turks and Caicos Islands|+1-649|USD|توركسي|Turks and Caicos Islander
        AW|ABW|أروبا|Aruba|+297|AWG|أروبي|Aruban
        CW|CUW|كوراساو|Curaçao|+599|ANG|كوراساوي|Curaçaoan
        SX|SXM|سانت مارتن الهولندية|Sint Maarten|+1-721|ANG|سانت مارتني|Sint Maartener
        BQ|BES|الجزر الكاريبية الهولندية|Caribbean Netherlands|+599|USD|هولندي|Dutch
        MF|MAF|سانت مارتن الفرنسية|Saint Martin|+590|EUR|سانت مارتني|Saint-Martinoise
        BL|BLM|سان بارتيلمي|Saint Barthélemy|+590|EUR|بارتيلمي|Barthélemois
        GP|GLP|غوادلوب|Guadeloupe|+590|EUR|غوادلوبي|Guadeloupean
        MQ|MTQ|مارتينيك|Martinique|+596|EUR|مارتينيكي|Martinican
        GF|GUF|غويانا الفرنسية|French Guiana|+594|EUR|غوياني|French Guianese
        PM|SPM|سان بيير وميكلون|Saint Pierre and Miquelon|+508|EUR|سان بييري|Saint-Pierrais
        RE|REU|ريونيون|Réunion|+262|EUR|ريونيوني|Réunionese
        YT|MYT|مايوت|Mayotte|+262|EUR|مايوتي|Mahoran
        NC|NCL|كاليدونيا الجديدة|New Caledonia|+687|XPF|كاليدوني|New Caledonian
        PF|PYF|بولينيزيا الفرنسية|French Polynesia|+689|XPF|بولينيزي|French Polynesian
        WF|WLF|واليس وفوتونا|Wallis and Futuna|+681|XPF|والسي|Wallisian
        GU|GUM|غوام|Guam|+1-671|USD|غوامي|Guamanian
        MP|MNP|جزر ماريانا الشمالية|Northern Mariana Islands|+1-670|USD|مارياني|Northern Marianan
        AS|ASM|ساموا الأمريكية|American Samoa|+1-684|USD|ساموي أمريكي|American Samoan
        UM|UMI|جزر الولايات المتحدة الصغيرة النائية|U.S. Minor Outlying Islands|+1|USD|أمريكي|American
        CK|COK|جزر كوك|Cook Islands|+682|NZD|كوكي|Cook Islander
        NU|NIU|نييوي|Niue|+683|NZD|نييوي|Niuean
        TK|TKL|توكيلاو|Tokelau|+690|NZD|توكيلاوي|Tokelauan
        PN|PCN|جزر بيتكيرن|Pitcairn Islands|+64|NZD|بيتكيرني|Pitcairn Islander
        NF|NFK|جزيرة نورفولك|Norfolk Island|+672|AUD|نورفولكي|Norfolk Islander
        CX|CXR|جزيرة كريسماس|Christmas Island|+61|AUD|كريسماسي|Christmas Islander
        CC|CCK|جزر كوكوس|Cocos (Keeling) Islands|+61|AUD|كوكوسي|Cocos Islander
        HM|HMD|جزيرة هيرد وجزر ماكدونالد|Heard Island and McDonald Islands|+672|AUD|أسترالي|Australian
        IO|IOT|إقليم المحيط الهندي البريطاني|British Indian Ocean Territory|+246|USD|بريطاني|British
        SH|SHN|سانت هيلينا|Saint Helena|+290|SHP|هيليني|Saint Helenian
        FK|FLK|جزر فوكلاند|Falkland Islands|+500|FKP|فوكلاندي|Falkland Islander
        GS|SGS|جورجيا الجنوبية وجزر ساندويتش|South Georgia and the South Sandwich Islands|+500|GBP|بريطاني|British
        TF|ATF|الأقاليم الجنوبية الفرنسية|French Southern Territories|+262|EUR|فرنسي|French
        BV|BVT|جزيرة بوفيه|Bouvet Island|+47|NOK|نرويجي|Norwegian
        AQ|ATA|القارة القطبية الجنوبية|Antarctica|+672|USD|قطبي|Antarctic
        """;
}
