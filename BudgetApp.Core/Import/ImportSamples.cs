namespace BudgetApp.Import
{
    /// <summary>
    /// Small, made-up example files in each bank's real layout. Used by the tests and the app's
    /// self-test to check that every format is read correctly. (All names and IBANs are fictional.)
    /// </summary>
    public static class ImportSamples
    {
        public const string Ing =
            "\"Datum\",\"Naam / Omschrijving\",\"Rekening\",\"Tegenrekening\",\"Code\",\"Af Bij\",\"Bedrag (EUR)\",\"Mutatiesoort\",\"Mededelingen\",\"Saldo na mutatie\",\"Tag\"\r\n" +
            "\"20261001\",\"Albert Heijn 1234\",\"NL11INGB0001234567\",\"\",\"BA\",\"Af\",\"23,45\",\"Betaalautomaat\",\"Pasvolgnr: 001 01-10-2026 17:32 Transactie: 1A2B3C Term: AB1234\",\"1234,56\",\"\"\r\n" +
            "\"20261001\",\"Werkgever BV\",\"NL11INGB0001234567\",\"NL22RABO0123456789\",\"OV\",\"Bij\",\"2150,00\",\"Overschrijving\",\"Naam: Werkgever BV Omschrijving: Salaris september IBAN: NL22RABO0123456789\",\"3384,56\",\"\"\r\n" +
            "\"20261002\",\"Netflix International B.V.\",\"NL11INGB0001234567\",\"NL33ABNA0123456789\",\"IC\",\"Af\",\"13,99\",\"Incasso\",\"Naam: Netflix International B.V. Omschrijving: Abonnement oktober\",\"3370,57\",\"\"\r\n" +
            "\"20261003\",\"Oranje Spaarrekening V12345678\",\"NL11INGB0001234567\",\"NL44INGB0009876543\",\"GT\",\"Af\",\"200,00\",\"Online bankieren\",\"Naar Oranje Spaarrekening V12345678\",\"3170,57\",\"\"\r\n" +
            "\"20261005\",\"Huisbaas Jansen\",\"NL11INGB0001234567\",\"NL55INGB0001112223\",\"OV\",\"Af\",\"650,00\",\"Overschrijving\",\"Naam: Huisbaas Jansen Omschrijving: Huur oktober\",\"2520,57\",\"\"\r\n" +
            "\"20261005\",\"Espresso Bar Centraal\",\"NL11INGB0001234567\",\"\",\"BA\",\"Af\",\"3,50\",\"Betaalautomaat\",\"Pasvolgnr: 001 05-10-2026 08:10\",\"2517,07\",\"\"\r\n";

        public const string IngSemicolonEnglish =
            "\"Date\";\"Name / Description\";\"Account\";\"Counterparty\";\"Code\";\"Debit/credit\";\"Amount (EUR)\";\"Transaction type\";\"Notifications\";\"Resulting balance\";\"Tag\"\n" +
            "\"20261008\";\"Jumbo Maastricht\";\"NL11INGB0001234567\";\"\";\"BA\";\"Debit\";\"41,20\";\"Payment terminal\";\"Card sequence no.: 001\";\"2475,87\";\"\"\n" +
            "\"20261009\";\"T. de Vries\";\"NL11INGB0001234567\";\"NL66INGB0004445556\";\"GT\";\"Credit\";\"15,00\";\"Online Banking\";\"Name: T. de Vries Description: pizza\";\"2490,87\";\"\"\n";

        public const string Rabobank =
            "\"IBAN/BBAN\",\"Munt\",\"BIC\",\"Volgnr\",\"Datum\",\"Rentedatum\",\"Bedrag\",\"Saldo na trn\",\"Tegenrekening IBAN/BBAN\",\"Naam tegenpartij\",\"Naam uiteindelijke partij\",\"Naam initiërende partij\",\"BIC tegenpartij\",\"Code\",\"Batch ID\",\"Transactiereferentie\",\"Machtigingskenmerk\",\"Incassant ID\",\"Betalingskenmerk\",\"Omschrijving-1\",\"Omschrijving-2\",\"Omschrijving-3\",\"Reden retour\",\"Oorspr bedrag\",\"Oorspr munt\",\"Koers\"\r\n" +
            "\"NL00RABO1234567890\",\"EUR\",\"RABONL2U\",\"000000000000006408\",\"2026-09-01\",\"2026-09-01\",\"+442,98\",\"+3436,04\",\"NL01RABO1234567890\",\"Café Één\",\"\",\"\",\"RABONL2U\",\"cb\",\"\",\"NY00012345678901234567890\",\"\",\"\",\"\",\"Terugbetaling weekend\",\" \",\"\",\"\",\"\",\"\",\"\"\r\n" +
            "\"NL00RABO1234567890\",\"EUR\",\"RABONL2U\",\"000000000000006409\",\"2026-09-01\",\"2026-09-01\",\"-30,00\",\"+3406,04\",\"NL01RABO9999999999\",\"Basic-Fit Nederland B.V.\",\"\",\"Rabobank Nederland APO\",\"RABONL2UXXX\",\"ei\",\"\",\"\",\"\",\"\",\"\",\"Lidmaatschap september\",\" \",\"\",\"\",\"\",\"\",\"\"\r\n" +
            "\"NL00RABO1234567890\",\"EUR\",\"RABONL2U\",\"000000000000006410\",\"2026-09-02\",\"2026-09-02\",\"-1.435,00\",\"+1971,04\",\"NL02ABNA0000000001\",\"Woningstichting Maas\",\"\",\"\",\"ABNANL2A\",\"bg\",\"\",\"\",\"\",\"\",\"\",\"Huur september\",\"Woning 12\",\"\",\"\",\"\",\"\",\"\"\r\n";

        public const string AbnAmro =
            "123456789\tEUR\t20261012\t3025,00\t3000,00\t20261012\t-25,00\tBEA   NR:A1B23C   12.10.26/09.15 Hema EV123,PAS123               ZAANDAM                                                          \r\n" +
            "123456789\tEUR\t20261012\t3000,00\t3025,00\t20261012\t25,00\tBEA   NR:A1B23C   12.10.26/15.01 Hema EV123,PAS123               ZAANDAM                          TERUGBOEKING-BEA-TRANSACTIE     \r\n" +
            "123456789\tEUR\t20261013\t3025,00\t3011,01\t20261013\t-13,99\t/TRTP/SEPA Incasso algemeen doorlopend/CSID/NL00ZZZ123456789012/NAME/Spotify AB/MARF/X1/REMI/Spotify Premium/IBAN/NL00INGB0123456789/BIC/INGBNL2A/EREF/AB0123456789\r\n" +
            "123456789\tEUR\t20261014\t3011,01\t2961,01\t20261014\t-50,00\tSEPA Overboeking                 IBAN: NL01RABO0123456789        BIC: RABONL2U                    Naam: J de Boer                  Omschrijving: Verjaardag cadeau\r\n";
    }
}
