using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Spectre.Console;
using Newtonsoft.Json.Linq;

namespace DevToolBox
{
    public class Projet
    {
        public string Nom { get; set; }
        public string UrlRepos { get; set; }
        public string NomVM { get; set; }
        public string CleSSHVMPath { get; set; }
        public bool ScheduleUpdateSSH { get; set; }
    }
    internal class Program
    {
        public static void CheckSystemRequirements()
        {
            AnsiConsole.Status()
                .Start("Vérification des composants système...", ctx =>
                {
                    // Vérifier Hyper-V
                    ctx.Status("Vérification de Hyper-V...");
                    bool isHyperVInstalled = CheckHyperVInstalled();

                    // Vérifier Vagrant
                    ctx.Status("Vérification de Vagrant...");
                    bool isVagrantInstalled = CheckVagrantInstalled();

                    ctx.Status("Chargement...");

                    // Afficher les résultats
                    var table = new Table();

                    table.AddColumn("Composant");
                    table.AddColumn("Statut");

                    table.AddRow("[yellow]Hyper-V[/]", isHyperVInstalled ? "[green]Installé[/]" : "[red]Non installé[/]");
                    table.AddRow("[yellow]Vagrant[/]", isVagrantInstalled ? "[green]Installé[/]" : "[red]Non installé[/]");

                    AnsiConsole.Write(table);
                });
        }

        private static bool CheckHyperVInstalled()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-Command \"(Get-WindowsOptionalFeature -FeatureName Microsoft-Hyper-V-All -Online).State\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return string.Equals(output, "Enabled", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CheckVagrantInstalled()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c vagrant --version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return !string.IsNullOrWhiteSpace(output) && output.Contains("Vagrant");
            }
            catch (Exception)
            {
                return false;
            }
        }
        public static void createVM(Projet projet)
        {
            string vagrantFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VgToolBox","config", projet.Nom, "Vagrantfile");
            if (File.Exists(vagrantFilePath))
            {
                Process.Start("cmd.exe", $"/c cd {Path.GetDirectoryName(vagrantFilePath)} && vagrant up").WaitForExit();
                Console.WriteLine("VM créée avec succès.");
            }
            else
            {
                Console.WriteLine("Le fichier Vagrantfile n'existe pas. Vérifiez le chemin et réessayez.");
            }
        }

        private static bool DevVmExists(string vmName)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    // Utilisez l'interpolation de chaîne pour insérer vmName
                    Arguments = $"-Command \"Get-VM | Where-Object {{ $_.Name -eq '{vmName}' }}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return !string.IsNullOrEmpty(output);
        }

        public static string infoDevVM(string vmName)
        {
            var command = $"get-vm '{vmName}' | " +
                          $"select -ExpandProperty networkadapters | " +
                          $"select -ExpandProperty ipaddresses";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-Command \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Traitement de la sortie pour extraire les adresses IP
                var ipAddresses = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                                        .Where(ip => ip.Contains(".") && !ip.StartsWith("fe80::")) // Filtrer pour obtenir uniquement les adresses IPv4
                                        .FirstOrDefault(); // Prendre la première adresse IPv4 trouvée

                if (!string.IsNullOrEmpty(ipAddresses))
                {
                    return ipAddresses; // Retourne l'adresse IP trouvée
                }
                else
                {
                    Console.WriteLine($"Aucune adresse IPv4 trouvée pour la VM '{vmName}'. Assurez-vous que la VM est en cours d'exécution.");
                    return string.Empty; // Retourne une chaîne vide si aucune adresse IP n'est trouvée
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Une erreur est survenue lors de la récupération de l'adresse IP pour '{vmName}': {ex.Message}");
                return string.Empty; // Retourne une chaîne vide en cas d'erreur
            }
        }


        public static void LaunchWithAdminRights(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = fileName,
                Arguments = arguments,
                Verb = "runas" 
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // L'exception est lancée si l'utilisateur annule l'élévation de privilèges
                Console.WriteLine("Élévation de privilèges annulée par l'utilisateur.");
            }
        }
        private static void InitializeConfiguration()
        {
            // Construire le chemin vers le dossier %APPDATA%\VgToolBox\config
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string vgToolBoxPath = Path.Combine(appDataPath, "VgToolBox");
            string configPath = Path.Combine(vgToolBoxPath, "config");

            // Créer le dossier VgToolBox et le sous-dossier config si nécessaire
            if (!Directory.Exists(vgToolBoxPath))
            {
                Directory.CreateDirectory(vgToolBoxPath);
            }

            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }

            // Chemin du fichier JSON dans le dossier config
            string jsonFilePath = Path.Combine(configPath, "config.json");

            // Créer le fichier JSON s'il n'existe pas
            if (!File.Exists(jsonFilePath))
            {

                var initialConfig = new {};
                string jsonContent = JsonConvert.SerializeObject(initialConfig, Formatting.Indented);

                // Écrire le contenu JSON dans le fichier
                File.WriteAllText(jsonFilePath, jsonContent);
            }
        }
        static void welcomInterface()
        {
            // Créez l'objet FigletText
            var figletText = new FigletText("Aidalinfo DevToolBox Windows")
                                .Centered()
                                .Color(Color.Green);

           
            // Affichez le texte Figlet
            AnsiConsole.Render(figletText);
        }
        static void GestionConfig()
        {
            var choixConfig = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Menu Config")
                    .PageSize(10)
                    .AddChoices(new[] { "Ajouter un projet", "Charger une configuration", "Retour" }));

            switch (choixConfig)
            {
                case "Ajouter un projet":
                    addProject();
                    break;

                case "Charger une configuration":
                    Console.WriteLine("Comming Soon");
                    break;

                case "Retour":
                    // Retourne simplement au menu principal
                    break;
            }
        }
        static void addProject()
        {
            var projet = new Projet
            {
                Nom = AnsiConsole.Ask<string>("Quel est le [green]nom du projet[/]?"),
                UrlRepos = AnsiConsole.Ask<string>("Quelle est l'[green]URL du repos[/] avec le Vagrantfile?"),
                NomVM = AnsiConsole.Ask<string>("Quel est le [green]nom de la VM[/]?"),
                CleSSHVMPath = AnsiConsole.Ask<string>("Quel est le [green]chemin de la clé SSH[/] pour la VM?"),
                ScheduleUpdateSSH = AnsiConsole.Confirm("Planifier la [green]mise à jour SSH[/] au démarrage après le boot de la VM?")
            };

            saveProject(projet);
        }
        static void saveProject(Projet projet)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configPath = Path.Combine(appDataPath, "VgToolBox", "config");
            string fichierProjet = Path.Combine(configPath, "projets.json");

            List<Projet> projets;

            // S'assurer que le dossier existe
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }

            // Vérifier si le fichier existe et n'est pas vide
            if (File.Exists(fichierProjet) && new FileInfo(fichierProjet).Length > 0)
            {
                string jsonContent = File.ReadAllText(fichierProjet);
                try
                {
                    projets = JsonConvert.DeserializeObject<List<Projet>>(jsonContent);
                }
                catch (JsonSerializationException)
                {
                    // En cas d'erreur de désérialisation, initialiser une nouvelle liste
                    projets = new List<Projet>();
                }
            }
            else
            {
                // Si le fichier n'existe pas ou est vide, initialiser une nouvelle liste
                projets = new List<Projet>();
            }

            // Ajouter le nouveau projet à la liste
            projets.Add(projet);

            // Enregistrer la liste mise à jour dans le fichier JSON
            File.WriteAllText(fichierProjet, JsonConvert.SerializeObject(projets, Formatting.Indented));
            Console.WriteLine("Projet ajouté avec succès !");
            cloneGitRepository(projet, configPath);
        }
        static void cloneGitRepository(Projet projet, string configPath)
        {
            string dossierProjet = Path.Combine(configPath, projet.Nom);

            if (!Directory.Exists(dossierProjet))
            {
                Directory.CreateDirectory(dossierProjet);

                AnsiConsole.Status()
                    .Start("Clonage en cours...", ctx =>
                    {
                        ctx.Spinner(Spinner.Known.Star);
                        ctx.SpinnerStyle(Style.Parse("green"));

                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "git",
                            Arguments = $"clone {projet.UrlRepos} \"{dossierProjet}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        };

                        using (var process = Process.Start(startInfo))
                        {
                            process.WaitForExit();
                        }

                        ctx.Status("Clonage terminé.");
                    });
            }
            else
            {
                Console.WriteLine($"Le dossier pour le projet {projet.Nom} existe déjà.");
            }
        }

        public static void listProject()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configPath = Path.Combine(appDataPath, "VgToolBox", "config");
            string fichierProjet = Path.Combine(configPath, "projets.json");

            if (File.Exists(fichierProjet))
            {
                string jsonContent = File.ReadAllText(fichierProjet);
                var projets = JsonConvert.DeserializeObject<List<Projet>>(jsonContent);

                if (projets == null || projets.Count == 0)
                {
                    Console.WriteLine("Aucun projet trouvé.");
                    return;
                }

                var choixProjet = AnsiConsole.Prompt(
                    new SelectionPrompt<Projet>()
                        .Title("Sélectionnez un projet:")
                        .PageSize(10)
                        .UseConverter(p => p.Nom)
                        .AddChoices(projets));

                Console.WriteLine($"Projet sélectionné: {choixProjet.Nom}");

                if (DevVmExists(choixProjet.NomVM))
                {
                    var action = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("La VM existe, que souhaitez-vous faire ?")
                            .PageSize(10)
                            .AddChoices(new[] { "Add to SSH vscode", "Connect to SSH", "Destroy Vagrant" }));

                    switch (action)
                    {
                        case "Add to SSH vscode":
                            // Implémentez l'ajout à SSH pour vscode ici
                            Console.WriteLine("Ajout à SSH vscode...");
                            UpdateSSHConfig(choixProjet.Nom, infoDevVM(choixProjet.NomVM), choixProjet.CleSSHVMPath);
                            break;
                        case "Connect to SSH":
                            // Implémentez la connexion SSH ici
                            Console.WriteLine("Connexion SSH...");
                            connectToSSH(choixProjet.NomVM, choixProjet.CleSSHVMPath);
                            break;
                        case "Destroy Vagrant":
                            // Implémentez la destruction de la VM Vagrant ici
                            Console.WriteLine("Destruction de la VM Vagrant...");
                            destroyVagrantVM(choixProjet.Nom);
                            break;
                    }
                }
                else
                {
                    var createVm = AnsiConsole.Confirm("La VM n'existe pas. Voulez-vous la créer ?");
                    if (createVm)
                    {
                        Console.WriteLine("Création de la VM via Vagrant...");
                        createVM(choixProjet);
                    }
                }
            }
            else
            {
                Console.WriteLine("Le fichier de projets n'existe pas.");
            }
        }
        public static void destroyVagrantVM(string vmName)
        {
            string vagrantFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VgToolBox", "config", vmName);
            if (Directory.Exists(vagrantFilePath))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-NoExit -Command \"cd '" + vagrantFilePath + "'; vagrant destroy -f\"",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
            }
            else
            {
                Console.WriteLine($"Le dossier pour le projet {vmName} n'existe pas ou le Vagrantfile est manquant.");
            }
        }


        public static void connectToSSH(string vmName, string sshKeyPath)
        {
            string ipAddress = infoDevVM(vmName); 

            if (!string.IsNullOrEmpty(ipAddress))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoExit -Command \"& 'ssh' -i '{sshKeyPath}' vagrant@{ipAddress}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Console.WriteLine("Impossible de récupérer l'adresse IP de la VM ou la VM n'est pas en cours d'exécution.");
            }
        }
        public static void UpdateSSHConfig(string projectName, string vmIp, string keyPath)
        {
            string sshConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

            if (!File.Exists(sshConfigPath))
            {
                Console.WriteLine("Le fichier de configuration SSH n'existe pas. Création d'un nouveau fichier.");
                File.WriteAllText(sshConfigPath, "");
            }

            var lines = File.ReadAllLines(sshConfigPath).ToList();
            bool projectFound = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains($"Host {projectName}"))
                {
                    projectFound = true;
                    // Mise à jour des informations de configuration existantes
                    UpdateConfigLines(ref lines, ref i, vmIp, keyPath);
                    break;
                }
            }

            if (!projectFound)
            {
                // Ajouter une nouvelle entrée de configuration si le projet n'est pas trouvé
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"### {projectName} ###");
                sb.AppendLine($"Host {projectName}");
                sb.AppendLine($"  HostName {vmIp}");
                sb.AppendLine($"  User vagrant");
                sb.AppendLine($"  IdentityFile {keyPath}");
                lines.AddRange(sb.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None));
            }

            File.WriteAllLines(sshConfigPath, lines);
            Console.WriteLine($"Configuration SSH mise à jour pour le projet {projectName}.");
        }

        private static void UpdateConfigLines(ref List<string> lines, ref int index, string vmIp, string keyPath)
        {
            for (int j = index + 1; j <= index + 3 && j < lines.Count; j++)
            {
                if (lines[j].TrimStart().StartsWith("HostName"))
                {
                    lines[j] = $"  HostName {vmIp}";
                }
                else if (lines[j].TrimStart().StartsWith("IdentityFile"))
                {
                    lines[j] = $"  IdentityFile {keyPath}";
                }
            }
        }
        public static async Task CheckForNewVersionAsync()
        {
            Version localversion = Assembly.GetExecutingAssembly().GetName().Version;
            string currentVersion = $"{localversion.Minor}.{localversion.Build}.{localversion.Revision}";
            Console.WriteLine($"The current version is {currentVersion}");
            var latestVersion = await GetLatestVersionFromGitHubAsync("aidalinfo", "DevToolBoxWindows");

            if (latestVersion != null && new Version(latestVersion) > new Version(currentVersion))
            {
                AnsiConsole.MarkupLine($"[green]Une nouvelle version est disponible : {latestVersion}. Votre version : {currentVersion}[/]");
                System.Threading.Thread.Sleep(10000);
            }
            else
            {
                AnsiConsole.MarkupLine("[blue]Votre application est à jour.[/]");
            }
        }

        private static async Task<string> GetLatestVersionFromGitHubAsync(string owner, string repo)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "request");

                var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                try
                {
                    var response = await client.GetStringAsync(url);
                    var jsonObject = JObject.Parse(response);
                    return jsonObject["tag_name"].ToString().TrimStart('v');
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Erreur lors de la vérification de la nouvelle version : {ex.Message}[/]");
                    return null;
                }
            }
        }
        static void Main(string[] args)
        {
            InitializeConfiguration();
            bool exit = false;
            CheckForNewVersionAsync();
            while (!exit)
            {
                Console.Clear();
                welcomInterface();
                CheckSystemRequirements();

                var choix = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Menu")
                        .PageSize(10)
                        .AddChoices(new[] { "Projet", "Config", "Quitter" }));

                switch (choix)
                {
                    case "Projet":
                        AnsiConsole.WriteLine("Gestion des projets...");
                        listProject();
                        break;

                    case "Config":
                        GestionConfig();
                        break;

                    case "Quitter":
                        exit = true;
                        break;
                }
            }
        }
    }
}
