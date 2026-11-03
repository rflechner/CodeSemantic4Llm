# Journaux MCP et dépannage

[English](logging.md) · **Français**

Activez la journalisation dans un fichier au lancement du serveur :

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  serve /path/to/repository --log-file /path/to/logs/codesemantic4llm.jsonl
```

Pour un client MCP, ajoutez l’option aux arguments de lancement de la DLL compilée :

```json
{
  "command": "dotnet",
  "args": [
    "/absolute/path/to/CodeSemantic4Llm.Mcp.Server.dll",
    "serve",
    "/absolute/path/to/repository",
    "--log-file",
    "/absolute/path/to/logs/codesemantic4llm.jsonl"
  ]
}
```

Redémarrez le serveur après modification des arguments. Sans `--log-file`, aucun journal fichier n’est produit. Cette option est indépendante de `--expose-indexing` et `--allow-write` et n’est exposée ni comme outil ni comme paramètre du modèle. Les journaux console vont sur stderr. Stdout est réservé au protocole MCP.

Les chemins relatifs des journaux sont résolus depuis le **répertoire de lancement initial**.
Même lorsque `serve <repository-path>` change le répertoire courant du processus,
la destination des journaux reste ancrée à ce répertoire initial. Les dossiers parents absents sont créés. Un chemin absolu hors du dépôt convient lorsque les clients utilisent différents répertoires de travail. Si le fichier ne peut pas être ouvert, le démarrage échoue avec un message sur stderr. Utilisez un fichier différent pour chaque serveur démarré simultanément.

## Analyser un échec

Le fichier contient du JSON UTF-8, un enregistrement par ligne et par événement. Chaque écriture est vidée immédiatement pour permettre la lecture pendant l’exécution :

```sh
tail -f /path/to/logs/codesemantic4llm.jsonl
```

| Champ | Contenu |
| --- | --- |
| `timestamp` | Horodatage UTC au format ISO 8601 |
| `processId`, `category`, `level`, `eventId` | Processus, journal et catégorie d’événement |
| `requestId`, `toolName` | Identifiants permettant de suivre un appel |
| `message` | Début, résultat et durée en millisecondes, ou détails de l’application |
| `exception` | Type, message, pile et éventuelles exceptions internes |
| `parameters` | Objet complet reçu pour `tools/call`, dont `name` et `arguments` |
| `response` | Réponse JSON-RPC sortante avec `id` et `result` ou `error`, y compris les erreurs du SDK MCP. Les champs `content`, `structuredContent` et `isError` sont conservés lorsqu’ils existent. |

Les charges utiles sont des objets JSON et valent `null` sur les autres enregistrements. Regroupez paramètres, résultats, exceptions et réponses par `requestId` et `processId`. Les paramètres reflètent ce que le client a transmis ; les valeurs par défaut ne complètent pas les arguments absents. La réponse est enregistrée juste avant son envoi. Cela ne prouve pas sa réception.

Repérez le `requestId` de l’outil en échec et lisez tous les événements du même processus associés à cet identifiant. L’application peut consigner la cause technique avant de renvoyer un message simplifié au modèle. Les requêtes suivantes restent enregistrées. Les résultats d’erreur MCP comptent également comme des échecs. Un début sans résultat correspondant aide à repérer un appel interrompu ou bloqué.

`--log-file` conserve les paramètres et réponses complets, extraits de code compris, **sans masquage ni troncature**. Les charges utiles vont uniquement dans le fichier choisi. Les autres messages MCP (initialisation, notifications) ne sont pas capturés en tant que charges utiles. Le niveau minimum est Information et, pour le transport du SDK, Warning. Les arguments, résultats et exceptions peuvent contenir des informations sensibles : relisez le journal avant de le partager. Il est distinct du stockage de l’index.

## Conservation et incidents

Le serveur ajoute les événements au fichier existant après redémarrage. Avant qu’un nouvel événement ne fasse dépasser 10 Mio, le fichier passe en `.1` et les anciennes archives en `.2` et `.3`. Le fichier courant et trois archives sont conservés. Un événement exceptionnellement grand peut dépasser le seuil : aucun enregistrement n’est fractionné ni tronqué.

Les écritures sont vidées vers le système d’exploitation, sans garantie de persistance physique en cas de coupure électrique. Si une écriture ou une rotation échoue, la journalisation fichier est désactivée et l’erreur signalée une fois sur stderr. Les opérations et réponses MCP continuent. Corrigez la destination et redémarrez le serveur pour reprendre la journalisation.
