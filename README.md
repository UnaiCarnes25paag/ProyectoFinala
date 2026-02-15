## Funtzionalitate nagusiak

- **Teknologia**: WPF bezeroa (.NET 8, C# 12) eta TCP zerbitzari propioa (`Casino.Server`, 5000 portuan). SQLite lokalarekin (`%LocalAppData%\Casino\casino.db`), kanpoan ezer instalatu gabe.
- **Datu-basea**: Ez da eskuzko pausorik behar. Bezeroa edo zerbitzaria abiaraztean `DatabaseInitializer.EnsureCreated()` exekutatzen da eta taulak sortzen dira; `Chips` zutabea ere gehitzen da falta bada.
- **Hasierako erabiltzailea**: Automatikoki sortzen da `admin` / `admin` 5000 fitxarekin.
- **Autentifikazioa**: Saioa hasi lehendik dagoen erabiltzailearekin edo erregistro pantailatik kontu berria sortu. Pasahitzak SHA-256 bidez hasheatzen dira (demo xedeetarako sinplea).
- **Zerbitzaria**: Exekutatu `Casino.Server` proiektua; `127.0.0.1:5000` helbidean entzuten du. Testu-komandoak kudeatzen ditu (LOGIN, CREATE_TABLE, JOIN_TABLE, SET_READY, PLAYER_ACTION, HISTORY…).
- **Bezeroa**: Exekutatu WPF aplikazioa `Casino`. Logina egin ondoren: mahaia sortu edo batu, txata erabili eta “Prest” markatu. Jokalariek prest daudenean, zerbitzariak karta banaketa eta faseak (preflop, flop, turn, river) kudeatzen ditu.
- **Fitxak eta saldoa**: Erabiltzaile bakoitzak saldo iraunkorra du `Users.Chips` taulan; esku bakoitza amaitzean zerbitzariak eguneratzen du.
- **Historia eta estatistikak**: Bezeroak eskuen historia eskatzen du (HISTORY) eta estatistika-panela bistaratzen du (irabazi-tasa, gehien jokatutako eskuak…), PDF sinple batera esportatzeko aukerarekin.
- **Mahaiko txata**: Mahai bakoitzak txat sinplea du; bezeroak `POLL_STATE` bidaltzen du eta mezutegi berriak jasotzen ditu.
- **Mugak**: Gehienez 6 jokalari mahaiko. Zerbitzariak 10/20 itsu txiki/handi ezartzen ditu (bezeroan 50/100 ikusizko balio gisa).
- **Aurrebaldintzak**: .NET 8 SDK nahikoa da. Ez da EF, ezta kanpoko tresnarik erabiltzen, eta SQLite paketearen bidez dator.
