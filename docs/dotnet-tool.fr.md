# Installer CodeSemantic4Llm comme outil .NET

[English](dotnet-tool.md) · **Français**

CodeSemantic4Llm est une **application en ligne de commande et un serveur MCP local sur stdio**. Le paquet NuGet **`CodeSemantic4Llm.Mcp.Server`** est distribué sur le flux GitHub Packages de `rflechner`. L’exécutable se nomme **`codesemantic4llm`**. Pour une installation locale au projet, versionnez un manifeste d’outils .NET. Le paquet ne s’ajoute pas avec `<PackageReference>` dans le `.csproj` de l’application.

## Prérequis

- **SDK .NET 10**. L’outil cible `net10.0` et utilise le runtime partagé ASP.NET Core.
- Git et une copie de travail sur une branche nommée pour indexer le code.
- Compte GitHub et **jeton d’accès personnel classique** avec `read:packages`.

## 1. Configurer le flux NuGet

Une fois par développeur, enregistrez le flux dans la configuration NuGet de votre compte, sauf s’il y figure déjà :

```sh
dotnet nuget add source "https://nuget.pkg.github.com/rflechner/index.json" --name CodeSemanticGitHub
```

Un jeton classique avec `read:packages` est nécessaire même pour les paquets publics. Préférez une variable d’environnement au stockage du jeton dans un fichier. Exemple avec PowerShell 7 :

```powershell
$packagesPat = Read-Host "GitHub PAT (classic, read:packages)" -MaskInput
$env:NuGetPackageSourceCredentials_CodeSemanticGitHub = "Username=YOUR_GITHUB_LOGIN;Password=$packagesPat;ValidAuthenticationTypes=Basic"
Remove-Variable packagesPat
```

Remplacez `YOUR_GITHUB_LOGIN` par le compte propriétaire du jeton. NuGet lit la variable `NuGetPackageSourceCredentials_{sourceName}` que vous définissez. `CodeSemanticGitHub` correspond au nom donné à `add source`, et non à l’URL. Respectez sa casse, particulièrement sous Linux. [Identifiants NuGet par variable d’environnement](https://learn.microsoft.com/en-us/nuget/consume-packages/consuming-packages-authenticated-feeds#credentials-in-environment-variables)

Installez, mettez à jour ou restaurez dans le même terminal. Redéfinissez la variable pour chaque nouvelle session. Gardez les identifiants hors du dépôt. [Authentification GitHub Packages](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-to-github-packages)

## 2. Installer l’outil

### Installation recommandée dans le dépôt

À la racine de **votre** dépôt :

```sh
dotnet new tool-manifest
dotnet tool install CodeSemantic4Llm.Mcp.Server --local
dotnet codesemantic4llm --help
```

Si `.config/dotnet-tools.json` existe déjà, sautez `dotnet new tool-manifest`. Commitez le manifeste pour partager la même version. Chaque contributeur s’authentifie, puis lance `dotnet tool restore`. N’ajoutez pas `CodeSemantic4Llm.Mcp.Server` aux dépendances de l’application.

### Installation globale pour votre compte

```sh
dotnet tool install --global CodeSemantic4Llm.Mcp.Server
codesemantic4llm --help
```

La commande globale est `codesemantic4llm`. Son lanceur se trouve dans `%USERPROFILE%\.dotnet\tools` sous Windows et `$HOME/.dotnet/tools` sous Linux/macOS. Ajoutez ce dossier au `PATH` du client MCP ou renseignez le chemin absolu du lanceur dans le JSON.

Pour mettre à jour :

```sh
dotnet tool update CodeSemantic4Llm.Mcp.Server --local
# Ou pour une installation globale :
dotnet tool update CodeSemantic4Llm.Mcp.Server --global
```

## 3. Initialiser et indexer le dépôt

Les exemples utilisent l’installation locale depuis la racine du dépôt. Pour une installation globale, remplacez `dotnet codesemantic4llm` par `codesemantic4llm`.

```sh
dotnet codesemantic4llm init . --name "My Repository" --analyzer dotnet-csharp
dotnet codesemantic4llm preview . --max-files 50
dotnet codesemantic4llm index .
```

`init` crée `codesemantic4llm.yaml` à la racine. Si le YAML existe, passez cette étape. L’analyseur disponible est `dotnet-csharp` et `semanticIndexing.language` vaut `en` par défaut. L’indexeur persiste des métadonnées dérivées dans un cache SQLite par branche. Les fichiers source restent dans la copie de travail.

Relancez `index` après une modification du code, un changement de branche ou une édition via MCP. Le serveur MCP ne reconstruit pas l’index au démarrage. Pour un cache personnalisé, donnez à `CODESEMANTIC4LLM_INDEX_ROOT` le **même chemin absolu** pour `index`, `dashboard` et le client MCP. [Configuration du dépôt](repository-configuration.fr.md) · [Indexation](indexing.fr.md)

## 4. Configurer un client MCP

Le transport est **stdio**. Le client lance `serve <repository-path>`. Évitez `dotnet run` dans ce contexte, car la sortie de compilation peut corrompre le protocole. Les exemples démarrent en **lecture seule**.

### VS Code et GitHub Copilot Chat avec un outil local

Enregistrez ce fichier dans **`.vscode/mcp.json`** dans le dépôt contenant le manifeste `.config/dotnet-tools.json` :

```json
{
  "servers": {
    "codeSemantic4Llm": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["codesemantic4llm", "serve", "${workspaceFolder}"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

Pour une installation **globale**, utilisez `"command": "codesemantic4llm"` et `"args": ["serve", "${workspaceFolder}"]`. Vous pouvez omettre `cwd`.

Si le cache est personnalisé, ajoutez ce champ `env` **dans l’objet serveur** de l’exemple précédent :

```json
"env": {
  "CODESEMANTIC4LLM_INDEX_ROOT": "/absolute/path/to/indexes"
}
```

Après modification du JSON, redémarrez le serveur MCP dans VS Code et acceptez la demande de confiance.

### GitHub Copilot CLI

Enregistrez ce contenu dans **`.github/mcp.json`** dans votre dépôt, ou dans `~/.copilot/mcp-config.json` pour une configuration personnelle :

```json
{
  "mcpServers": {
    "codeSemantic4Llm": {
      "type": "local",
      "command": "dotnet",
      "args": ["codesemantic4llm", "serve", "."],
      "tools": ["*"]
    }
  }
}
```

Lancez `copilot` **à la racine du dépôt** pour que `.` cible la copie de travail et que `dotnet` trouve le manifeste local. Pour l’outil global, utilisez `codesemantic4llm` avec `["serve", "."]`. Copilot CLI attend **`mcpServers`** et `type: "local"`, VS Code **`servers`** et `type: "stdio"`. Vous pouvez versionner le fichier si l’équipe fait confiance à l’outil. Copilot CLI demande de confirmer la confiance dans le dépôt avant de charger ses serveurs.

### Claude Desktop et clients utilisant mcpServers avec un outil global

```json
{
  "mcpServers": {
    "codeSemantic4Llm": {
      "command": "codesemantic4llm",
      "args": ["serve", "/absolute/path/to/your/repository"]
    }
  }
}
```

Remplacez le chemin absolu du dépôt. Si l’application ne trouve pas l’outil global, indiquez le chemin complet dans `command` (par exemple `C:\Users\YOUR_USER\.dotnet\tools\codesemantic4llm.exe` sous Windows, en échappant les barres obliques inverses en JSON). L’emplacement du fichier de configuration dépend du client.

### Options facultatives du serveur

Le serveur est en lecture seule par défaut. Ajoutez `--allow-write` à `args` pour enregistrer les outils d’édition. `--expose-indexing` expose les outils d’administration et de consultation de l’état de l’index. `--log-file /absolute/path/to/log.jsonl` enregistre les requêtes et réponses : conservez ce fichier en privé, car il peut contenir **du code source et des paramètres sensibles**. Redémarrez le client après modification.

## 5. Vérifier l’intégration

1. Lancez `dotnet codesemantic4llm --help` ou la commande globale.
2. Vérifiez la présence de `codesemantic4llm.yaml` et la réussite de `index` pour la branche actuelle.
3. Redémarrez le client MCP. `get_server_info` et `find_symbols` doivent figurer dans la liste des outils.
4. Demandez à l’agent de trouver une classe par son nom : il devrait utiliser `find_symbols`.
5. Pour examiner le cache, lancez `dotnet codesemantic4llm dashboard --port 5080` et ouvrez `http://127.0.0.1:5080/`.

### Dépannage

- **401/403 à l’installation** : vérifiez les identifiants et l’accès au paquet. [Guide d’authentification GitHub](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-to-github-packages)
- **Serveur absent de la liste** : vérifiez le format JSON du client, le `PATH`, le manifeste et le répertoire courant. Lancé à la main, `serve` attend qu’un client communique sur stdio.
- **Aucun résultat** : indexez la branche actuelle. Vérifiez le dépôt et `CODESEMANTIC4LLM_INDEX_ROOT`.
- **Échec d’autres restaurations NuGet** : NuGet peut interroger tous les flux. Configurez le mappage des sources de paquets ou retirez le flux inutilisé avec `dotnet nuget remove source CodeSemanticGitHub`.

## Préparer et promouvoir les paquets

Depuis la racine, lancez la même compilation NUKE que l’intégration continue :

```sh
dotnet run --project build/Build.csproj -- Pack
```

NUKE restaure la solution, compile en Release, exécute tous les tests et crée le paquet dans `artifacts/`. La version de base vient de `<Version>` dans `src/CodeSemantic4Llm.Mcp.Server/CodeSemantic4Llm.Mcp.Server.csproj`. Le [workflow CI](../.github/workflows/ci.yml) installe le paquet et vérifie `--help`, `init` et `preview` avant de conserver l’artefact 30 jours.

| Événement | Version | Destination |
| --- | --- | --- |
| Pull request | `<base>-alpha-<commit>` | Artefact Actions |
| Push sur `develop` | `<base>-beta-<commit>` | Actions et GitHub Packages |
| Push sur `main` | `<base>` | Actions et GitHub Packages |

Le suffixe contient les huit premiers caractères du SHA. Si la version stable précédente est déjà publiée, changez la version de base avant de fusionner sur `main` : les versions GitHub Packages sont immuables.

Pour publier sur NuGet.org, créez l’environnement GitHub `nuget-org` et configurez une stratégie de publication de confiance GitHub Actions sur NuGet.org pour ce dépôt, le workflow `publish-tool.yml` et cet environnement. Définissez le secret `NUGET_USER` avec le nom du compte NuGet.org. Des réviseurs obligatoires peuvent contrôler la promotion.

Dans Actions, ouvrez **Promote .NET tool to NuGet.org** puis, sous **Use workflow from**, sélectionnez `develop` pour une bêta (`X.Y.Z-beta-<SHA du commit sur 8 caractères>`) ou `main` pour une version stable (`X.Y.Z`). Entrez la version exacte du paquet préparé. Une version stable cible par défaut la dernière CI réussie après un push sur `main` ; pour une bêta, la version identifie le commit sur `develop`. Fournissez l’identifiant de l’exécution CI pour choisir une compilation stable antérieure. Le workflow vérifie l’exécution, l’identifiant du paquet, la version et le commit. Il transmet le paquet existant à NuGet.org avec un identifiant de courte durée. Si l’artefact a expiré, il récupère la version depuis GitHub Packages.

**Première publication** : GitHub crée initialement un paquet privé. Pour ouvrir l’installation à tous, sélectionnez `CodeSemantic4Llm.Mcp.Server` dans les réglages Packages du compte, puis rendez le paquet public. Avec les permissions granulaires, dépôt et paquet ont des visibilités indépendantes. Rendre le paquet public expose son contenu compilé et le retour au privé peut être impossible. Installer depuis le registre NuGet de GitHub exige toujours un jeton classique.

[Authentification NuGet GitHub](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry) · [MCP dans VS Code](https://code.visualstudio.com/docs/agents/reference/mcp-configuration) · [MCP dans Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers)

## Commandes de partage des résumés

L’outil fournit aussi `cache-export <repository-path> [--overwrite]`, `watch <repository-path>` et `setup-git <repository-path>`. Cette dernière installe un pilote local qui appelle `git-merge-summary <current|other> <ancestor> <current> <other>`. Consultez le [guide des résumés partagés](shared-summaries.fr.md) avant de choisir une politique de résolution de conflits, en particulier pour les rebasages ou du Markdown non commité.
