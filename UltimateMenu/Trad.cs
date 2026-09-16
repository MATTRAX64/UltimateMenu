using System.Collections.Generic;
using UnityEngine;

namespace UltimateMenu
{

    public enum Lang { Fr = 0, En = 1, De = 2, Es = 3, It = 4, Pt = 5, Ja = 6, Ru = 7 }

    public static class I18n
    {
        static readonly Dictionary<string, string[]> _dict = new Dictionary<string, string[]>();

        public static Lang Current
        {
            get => (Lang)PlayerPrefs.GetInt("MUM_Lang", (int)Lang.Fr);
            set { PlayerPrefs.SetInt("MUM_Lang", (int)value); PlayerPrefs.Save(); }
        }

        public static string T(string key)
            => _dict.TryGetValue(key, out var a) && (int)Current < a.Length ? a[(int)Current] : key;

        public static void Register(string key, string fr, string en, string de, string es,
                                     string it, string pt, string ja, string ru)
            => _dict[key] = new[] { fr, en, de, es, it, pt, ja, ru };

        public static bool Has(string key) => _dict.ContainsKey(key);
    }

    [DefaultExecutionOrder(-1000)]
    public static class Trad
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            void R(string k, string fr, string en, string de, string es, string it, string pt, string ja, string ru)
                => I18n.Register(k, fr, en, de, es, it, pt, ja, ru);

            R("Core.glass_select_mod", "Sélectionnez un mod dans la liste pour afficher sa configuration ici.", "Select a mod from the list to show its configuration here.", "Wähle einen Mod aus der Liste, um hier seine Einstellungen anzuzeigen.", "Selecciona un mod de la lista para ver aquí su configuración.", "Seleziona una mod dall’elenco per visualizzarne qui la configurazione.", "Selecione um mod na lista para mostrar sua configuração aqui.", "一覧からMODを選択すると、ここに設定が表示されます。", "Выберите мод в списке, чтобы увидеть его настройки здесь.");
            R("Core.glass_general_settings", "Réglages généraux", "General settings", "Allgemeine Einstellungen", "Ajustes generales", "Impostazioni generali", "Configurações gerais", "全般設定", "Общие настройки");
            R("Core.glass_welcome", "À vous de jouer.", "Make it yours.", "Dein Spiel. Dein Menü.", "A tu manera.", "A modo tuo.", "Do seu jeito.", "自分らしくプレイ。", "Играйте по-своему.");
            R("Core.glass_mods_hint", "Parcourir, activer et configurer vos mods", "Browse, enable and configure your mods", "Mods durchsuchen, aktivieren und konfigurieren", "Explora, activa y configura tus mods", "Sfoglia, attiva e configura le tue mod", "Explore, ative e configure seus mods", "MODの一覧・有効化・設定", "Просмотр, включение и настройка модов");
            R("Core.glass_settings_hint", "Apparence, langue, sons et interface VR", "Appearance, language, sounds and VR interface", "Darstellung, Sprache, Sounds und VR-Menü", "Apariencia, idioma, sonidos e interfaz VR", "Aspetto, lingua, suoni e interfaccia VR", "Aparência, idioma, sons e interface VR", "外観・言語・サウンド・VRメニュー", "Оформление, язык, звуки и VR-меню");
            R("Core.glass_support", "Soutenir", "Support", "Unterstützen", "Apoyar", "Supporta", "Apoiar", "支援する", "Поддержать");

            R("Core.pc_menu_size", "Taille du menu PC", "PC menu size", "PC-Menügröße", "Tamaño del menú PC", "Dimensione menu PC", "Tamanho do menu PC", "PCメニューのサイズ", "Размер меню ПК");
            R("Core.pc_reset_position", "Recentrer la fenêtre PC", "Center the PC window", "PC-Fenster zentrieren", "Centrar la ventana PC", "Centra la finestra PC", "Centralizar a janela PC", "PCウィンドウを中央に戻す", "Центрировать окно ПК");
            R("Core.unavailable", "Indisponible", "Unavailable", "Nicht verfügbar", "No disponible", "Non disponibile", "Indisponível", "利用不可", "Недоступно");
            R("Core.pc_keyboard", "Clavier PC", "PC keyboard", "PC-Tastatur", "Teclado PC", "Tastiera PC", "Teclado PC", "PCキーボード", "Клавиатура ПК");
            R("Core.tab_home", "Accueil", "Home", "Start", "Inicio", "Home", "Inicio", "ホーム", "Главная");
            R("Core.tab_modes", "Modes", "Mods", "Mods", "Mods", "Mod", "Mods", "MOD", "Моды");
            R("Core.home_title", "Mattrax Ultimate Menu", "Mattrax Ultimate Menu", "Mattrax Ultimate Menu", "Mattrax Ultimate Menu", "Mattrax Ultimate Menu", "Mattrax Ultimate Menu", "Mattrax Ultimate Menu", "Mattrax Ultimate Menu");
            R("Core.home_desc", "Mods pour streamers et joueurs Gorilla Tag.", "Mods for Gorilla Tag streamers and players.", "Mods fuer Gorilla Tag Streamer und Spieler.", "Mods para streamers y jugadores de Gorilla Tag.", "Mod per streamer e giocatori di Gorilla Tag.", "Mods para streamers e jogadores de Gorilla Tag.", "Gorilla Tagのストリーマーとプレイヤー向けMod。", "Моды для стримеров и игроков Gorilla Tag.");
            R("Core.home_author", "Par MATTRAX", "By MATTRAX", "Von MATTRAX", "Por MATTRAX", "Di MATTRAX", "Por MATTRAX", "作者: MATTRAX", "Автор: MATTRAX");
            R("Core.appearance", "Apparence", "Appearance", "Erscheinungsbild", "Apariencia", "Aspetto", "Aparencia", "外観", "Внешний вид");
            R("Core.controls", "Controles", "Controls", "Steuerung", "Controles", "Controlli", "Controles", "操作設定", "Управление");
            R("Core.personalization", "Personnalisation du menu", "Menu personalization", "Menü-Personalisierung", "Personalizacion del menu", "Personalizzazione del menu", "Personalizacao do menu", "メニューのカスタマイズ", "Персонализация меню");
            R("Core.size_label", "Taille :", "Size:", "Groesse:", "Tamano:", "Dimensione:", "Tamanho:", "サイズ：", "Размер:");
            R("Core.waiting_key", "Appuyez sur une touche...", "Press a key...", "Druecke eine Taste...", "Presiona una tecla...", "Premi un tasto...", "Pressione uma tecla...", "キーを押してください…", "Нажмите клавишу...");
            R("Core.cancel", "Annuler", "Cancel", "Abbrechen", "Cancelar", "Annulla", "Cancelar", "キャンセル", "Отмена");
            R("Core.menu_key", "Touche menu", "Menu key", "Menuetaste", "Tecla de menu", "Tasto menu", "Tecla de menu", "メニューキー", "Клавиша меню");
            R("Core.last_key", "Derniere :", "Last:", "Letzte:", "Ultima:", "Ultimo:", "Ultima:", "最後：", "Последняя:");

            // --- Disposition clavier : détection automatique (plus de sélection manuelle) ---
            R("Core.keyboard_label", "Clavier :", "Keyboard:", "Tastatur:", "Teclado:", "Tastiera:", "Teclado:", "キーボード：", "Клавиатура:");
            R("Core.keyboard_auto_hint",
                "Détectée automatiquement selon ton clavier système.",
                "Automatically detected from your system keyboard.",
                "Automatisch anhand deiner System-Tastatur erkannt.",
                "Detectada automáticamente según tu teclado del sistema.",
                "Rilevata automaticamente in base alla tastiera di sistema.",
                "Detectada automaticamente com base no teclado do sistema.",
                "システムのキーボードから自動検出されました。",
                "Автоматически определено по клавиатуре системы.");

            R("Core.change", "Changer", "Change", "Aendern", "Cambiar", "Cambia", "Alterar", "変更", "Изменить");
            R("Core.theme", "Theme", "Theme", "Design", "Tema", "Tema", "Tema", "テーマ", "Тема");
            R("Core.theme_current", "Theme actuel :", "Current theme:", "Aktuelles Design:", "Tema actual:", "Tema attuale:", "Tema atual:", "現在のテーマ：", "Текущая тема:");
            R("Core.custom_colors", "Couleurs personnalisees", "Custom colors", "Eigene Farben", "Colores personalizados", "Colori personalizzati", "Cores personalizadas", "カスタムカラー", "Пользовательские цвета");
            R("Core.save_colors", "Sauvegarder ces couleurs", "Save these colors", "Diese Farben speichern", "Guardar estos colores", "Salva questi colori", "Salvar estas cores", "この色を保存", "Сохранить эти цвета");
            R("Core.open_close_hint", "ouvrir/fermer", "open/close", "oeffnen/schliessen", "abrir/cerrar", "apri/chiudi", "abrir/fechar", "開く/閉じる", "открыть/закрыть");
            R("Core.modules_suffix", "module(s)", "module(s)", "Modul(e)", "modulo(s)", "modulo(i)", "modulo(s)", "モジュール", "модуль(и)");
            R("Core.mods_list", "Liste des mods", "Mods list", "Modliste", "Lista de mods", "Elenco mod", "Lista de mods", "MODリスト", "Список модов");
            R("Core.clear_search", "Effacer la recherche", "Clear search", "Suche löschen", "Borrar búsqueda", "Cancella ricerca", "Limpar pesquisa", "検索をクリア", "Очистить поиск");
            R("Core.search_label", "Recherche :", "Search:", "Suche:", "Buscar:", "Cerca:", "Buscar:", "検索：", "Поиск:");
            R("Core.no_mod_found", "Aucun mod trouve.", "No mod found.", "Kein Mod gefunden.", "No se encontro ningun mod.", "Nessun mod trovato.", "Nenhum mod encontrado.", "Modが見つかりません。", "Моды не найдены.");
            R("Core.close_footer", "fermer", "close", "schliessen", "cerrar", "chiudi", "fechar", "閉じる", "закрыть");
            R("Core.by_author", "Par :", "By:", "Von:", "Por:", "Di:", "Por:", "作者：", "Автор:");
            R("Core.open_notif", "Ouvrir :", "Open:", "Oeffnen:", "Abrir:", "Apri:", "Abrir:", "開く：", "Открыть:");
            R("Core.language", "Langue", "Language", "Sprache", "Idioma", "Lingua", "Idioma", "言語", "Язык");
            R("Core.bold_text_label", "Texte en gras", "Bold text", "Fetter Text", "Texto en negrita", "Testo in grassetto", "Texto em negrito", "太字テキスト", "Жирный текст");
            R("Core.color_accent", "Accent", "Accent", "Akzent", "Acento", "Accento", "Destaque", "アクセント", "Акцент");
            R("Core.color_bg2", "Fond 2", "Background 2", "Hintergrund 2", "Fondo 2", "Sfondo 2", "Fundo 2", "背景2", "Фон 2");
            R("Core.color_bg3", "Fond 3", "Background 3", "Hintergrund 3", "Fondo 3", "Sfondo 3", "Fundo 3", "背景3", "Фон 3");
            R("Core.color_text", "Texte", "Text", "Text", "Texto", "Testo", "Texto", "テキスト", "Текст");
            R("Core.color_dim", "Texte attenue", "Dim text", "Gedaempfter Text", "Texto atenuado", "Testo attenuato", "Texto atenuado", "淡色テキスト", "Приглушённый текст");
            R("Core.bg_transparency", "Transparence fond", "Background opacity", "Hintergrundtransparenz", "Transparencia de fondo", "Trasparenza sfondo", "Transparencia do fundo", "背景の透明度", "Прозрачность фона");
            R("Core.theme_sombre_vert", "Sombre Vert", "Dark Green", "Dunkelgruen", "Verde Oscuro", "Verde Scuro", "Verde Escuro", "ダークグリーン", "Тёмно-зелёная");
            R("Core.theme_clair", "Clair", "Light", "Hell", "Claro", "Chiaro", "Claro", "ライト", "Светлая");
            R("Core.theme_sombre_violet", "Sombre Violet", "Dark Purple", "Dunkellila", "Violeta Oscuro", "Viola Scuro", "Roxo Escuro", "ダークパープル", "Тёмно-фиолетовая");
            R("Core.theme_sombre_bleu", "Sombre Bleu", "Dark Blue", "Dunkelblau", "Azul Oscuro", "Blu Scuro", "Azul Escuro", "ダークブルー", "Тёмно-синяя");
            R("Core.theme_custom", "Custom", "Custom", "Benutzerdefiniert", "Personalizado", "Personalizzato", "Personalizado", "カスタム", "Пользовательская");
            R("Core.sound_enabled_label", "Sons du menu", "Menu sounds", "Menü-Sounds", "Sonidos del menú", "Suoni del menu", "Sons do menu", "メニューの効果音", "Звуки меню");
            R("Core.sound_volume_label", "Volume", "Volume", "Lautstärke", "Volumen", "Volume", "Volume", "音量", "Громкость");
            R("Core.donate_title", "Vous aimez le mod ?", "Enjoying the mod?", "Gefällt dir der Mod?", "¿Te gusta el mod?", "Ti piace la mod?", "Está gostando do mod?", "MODを楽しんでいますか？", "Нравится мод?");
            R("Core.donate_button", "♥ Faire un don sur Patreon", "♥ Support on Patreon", "♥ Auf Patreon unterstützen", "♥ Apoyar en Patreon", "♥ Supporta su Patreon", "♥ Apoiar no Patreon", "♥ Patreonでサポートする", "♥ Поддержать на Patreon");

            // Ajoutées : utilisées par GorillaCamera.cs (StatusPill générique) — absentes du fichier d'origine.
            R("Core.active", "Actif", "Active", "Aktiv", "Activo", "Attivo", "Ativo", "アクティブ", "Активно");
            R("Core.inactive", "Inactif", "Inactive", "Inaktiv", "Inactivo", "Inattivo", "Inativo", "非アクティブ", "Неактивно");

            // === Interface VR (menu en jeu world-space, joystick + trigger) — désormais
            // partie intégrante de Core/MenuUI, réglages affichés dans l'Accueil. Rangées
            // ici avec les autres clés Core.* pour rester cohérent. ===
            R("Core.vr_controls", "Stick droit : bas puis haut pour ouvrir, haut puis bas pour fermer. Stick gauche : déplacer le contour blanc ; gauche/droite sur un curseur pour régler sa valeur. Gâchette gauche : valider. Bouton X : retour. Les champs texte ouvrent un clavier.", "Right stick: down then up to open, up then down to close. Left stick: move the white outline; left/right on a slider to adjust it. Left trigger: confirm. X button: back. Text fields open a keyboard.", "Rechter Stick: runter/hoch öffnet, hoch/runter schließt. Linker Stick: weißen Rahmen bewegen; links/rechts verändert Regler. Linker Trigger: bestätigen. X: zurück. Textfelder öffnen eine Tastatur.", "Stick derecho: abajo/arriba abre, arriba/abajo cierra. Stick izquierdo: mover el borde blanco; izquierda/derecha ajusta los valores. Gatillo izquierdo: confirmar. X: volver. Los campos abren un teclado.", "Stick destro: giù/su apre, su/giù chiude. Stick sinistro: sposta il bordo bianco; sinistra/destra regola i cursori. Grilletto sinistro: conferma. X: indietro. I campi aprono una tastiera.", "Analógico direito: baixo/cima abre, cima/baixo fecha. Analógico esquerdo: mover a borda branca; esquerda/direita ajusta os valores. Gatilho esquerdo: confirmar. X: voltar. Campos abrem um teclado.", "右スティック：下→上で開く、上→下で閉じる。左スティック：白枠を移動、スライダーは左右で調整。左トリガー：決定。X：戻る。入力欄でキーボードを開きます。", "Правый стик: вниз/вверх — открыть, вверх/вниз — закрыть. Левый стик: белая рамка; влево/вправо — значение ползунка. Левый триггер: выбор. X: назад. Поля открывают клавиатуру.");
            R("Core.vr_space", "Espace", "Space", "Leerzeichen", "Espacio", "Spazio", "Espaço", "空白", "Пробел");
            R("Core.vr_erase", "Effacer", "Delete", "Löschen", "Borrar", "Cancella", "Apagar", "削除", "Удалить");
            R("Core.vr_confirm", "Valider", "Confirm", "Bestätigen", "Confirmar", "Conferma", "Confirmar", "決定", "Принять");
            R("Core.vr_section", "Interface VR (menu en jeu)", "VR Interface (in-game menu)", "VR-Schnittstelle (Ingame-Menü)", "Interfaz VR (menú en el juego)", "Interfaccia VR (menu in gioco)", "Interface VR (menu no jogo)", "VRインターフェース（ゲーム内メニュー）", "VR-интерфейс (меню в игре)");

            R("Core.vr_back_mods", "< Retour aux mods", "< Back to mods", "< Zurück zu Mods", "< Volver a los mods", "< Torna alle mod", "< Voltar aos mods", "< MOD一覧に戻る", "< Назад к модам");
            R("Core.vr_mod_enabled", "Mod actif", "Mod enabled", "Mod aktiviert", "Mod activado", "Mod attiva", "Mod ativado", "MOD有効", "Мод включён");
            R("Core.vr_settings", "Réglages", "Settings", "Einstellungen", "Ajustes", "Impostazioni", "Configurações", "設定", "Настройки");
            R("Core.vr_active", "Actif", "Active", "Aktiv", "Activo", "Attivo", "Ativo", "アクティブ", "Активно");

            R("Core.vr_size_short", "Échelle", "Scale", "Skalierung", "Escala", "Scala", "Escala", "スケール", "Масштаб");

            R("Core.vr_distance_short", "Distance", "Distance", "Distanz", "Distancia", "Distanza", "Distância", "距離", "Дистанция");


            R("Core.vr_animspeed_short", "Vitesse", "Speed", "Geschwindigkeit", "Velocidad", "Velocità", "Velocidade", "速度", "Скорость");



            R("Core.vr_sticksens_short", "Seuil", "Threshold", "Schwelle", "Umbral", "Soglia", "Limite", "しきい値", "Порог");
            R("Core.vr_shaketreshold_short", "Seuil de secousse", "Shake threshold", "Schnellheitschwelle", "Umbral de sacudida", "Soglia di scossa", "Limite de tremor", "振動のしきい値", "Порог тряски");
            R("Core.vr_shakewindow_short", "Fenêtre de secousse", "Shake window", "Schnellheitsfenster", "Ventana de sacudida", "Finestra di scossa", "Janela de tremor", "振動のウィンドウ", "Окно тряски");

            R("Category.Gameplay", "Gameplay", "Gameplay", "Gameplay", "Gameplay", "Gameplay", "Gameplay", "ゲームプレイ", "Геймплей");
            R("Category.Camera", "Caméra", "Camera", "Kamera", "Cámara", "Fotocamera", "Câmera", "カメラ", "Камера");
            R("Category.Apparence", "Apparence", "Appearance", "Erscheinungsbild", "Apariencia", "Aspetto", "Aparência", "外観", "Внешний вид");
            R("Category.Technique", "Technique", "Technical", "Technisch", "Técnico", "Tecnico", "Técnico", "テクニカル", "Технический");
            R("Category.Social", "Social", "Social", "Sozial", "Social", "Sociale", "Social", "ソーシャル", "Социальный");
            R("Category.Environnement", "Environnement", "Environment", "Umgebung", "Entorno", "Ambiente", "Ambiente", "環境", "Окружение");
            R("Category.Experimental", "Expérimental", "Experimental", "Experimentell", "Experimental", "Sperimentale", "Experimental", "実験的", "Экспериментальный");
            R("Category.Mocap", "Mocap", "Mocap", "Mocap", "Mocap", "Mocap", "Mocap", "モーションキャプチャ", "Mocap");
            R("Category.Stream", "Stream", "Stream", "Stream", "Stream", "Stream", "Stream", "ストリーム", "Стрим");
            R("Category.Cosmetique", "Cosmétique", "Cosmetic", "Kosmetik", "Cosmético", "Cosmetico", "Cosmético", "コスメ", "Косметика");
            R("Category.Tracking", "Tracking", "Tracking", "Tracking", "Seguimiento", "Tracciamento", "Rastreamento", "トラッキング", "Трекинг");

            // === Camera (mod caméra spectateur) ===
            R("Camera.Title", "Caméra Spectateur", "Spectator Camera", "Zuschauerkamera", "Cámara de espectador", "Fotocamera spettatore", "Câmera de espectador", "観戦カメラ", "Камера зрителя");
            R("Camera.Speed", "Vitesse", "Speed", "Geschwindigkeit", "Velocidad", "Velocità", "Velocidade", "速度", "Скорость");
            R("Camera.Sensitivity", "Sensibilité", "Sensitivity", "Empfindlichkeit", "Sensibilidad", "Sensibilità", "Sensibilidade", "感度", "Чувствительность");
            R("Camera.MoveKeysLabel", "Déplacement :", "Movement:", "Bewegung:", "Movimiento:", "Movimento:", "Movimento:", "移動：", "Движение:");
            R("Camera.UpDownSuffix", "(haut/bas)", "(up/down)", "(hoch/runter)", "(arriba/abajo)", "(su/giù)", "(cima/baixo)", "（上/下）", "(вверх/вниз)");
            R("Camera.AddKF", "Ajouter keyframe", "Add keyframe", "Keyframe hinzufügen", "Añadir keyframe", "Aggiungi keyframe", "Adicionar keyframe", "キーフレーム追加", "Добавить ключевой кадр");
            R("Camera.WaitingKey", "Appuyez sur une touche...", "Press a key...", "Druecke eine Taste...", "Presiona una tecla...", "Premi un tasto...", "Pressione uma tecla...", "キーを押してください…", "Нажмите клавишу...");
            R("Camera.EscapeCancel", "Échap pour annuler", "Escape to cancel", "Escape zum Abbrechen", "Escape para cancelar", "Escape per annullare", "Escape para cancelar", "Escで取り消し", "Escape для отмены");
            R("Camera.Keyframes", "Keyframes", "Keyframes", "Keyframes", "Keyframes", "Keyframes", "Keyframes", "キーフレーム", "Ключевые кадры");
            R("Camera.NoKeyframes", "Aucune keyframe.", "No keyframes.", "Keine Keyframes.", "Sin keyframes.", "Nessun keyframe.", "Nenhum keyframe.", "キーフレームがありません。", "Нет ключевых кадров.");
            R("Camera.Go", "Aller", "Go", "Los", "Ir", "Vai", "Ir", "移動", "Перейти");
            R("Camera.Duration", "Durée", "Duration", "Dauer", "Duración", "Durata", "Duração", "長さ", "Длительность");
            R("Camera.Mode", "Mode", "Mode", "Modus", "Modo", "Modalità", "Modo", "モード", "Режим");
            R("Camera.Loop", "Boucle", "Loop", "Schleife", "Bucle", "Loop", "Loop", "ループ", "Цикл");
            R("Camera.Start", "Lancer", "Start", "Start", "Iniciar", "Avvia", "Iniciar", "開始", "Начать");
            R("Camera.Stop", "Arrêter", "Stop", "Stopp", "Detener", "Ferma", "Parar", "停止", "Остановить");

            R("Mocap.Title", "Mocap", "Mocap", "Mocap", "Mocap", "Mocap", "Mocap", "モーションキャプチャ", "Mocap");
            R("Mocap.BlenderAssetHelp", "Importer dans Blender", "Import into Blender", "In Blender importieren", "Importar a Blender", "Importa in Blender", "Importar para o Blender", "Blenderにインポート", "Импорт в Blender");
            R("Mocap.BlenderAssetHint", "Un package d'aide se trouve dans ce dossier pour importer les enregistrements dans Blender.", "A helper package is in this folder to import recordings into Blender.", "In diesem Ordner befindet sich ein Hilfspaket, um Aufnahmen in Blender zu importieren.", "Hay un paquete de ayuda en esta carpeta para importar las grabaciones en Blender.", "In questa cartella è presente un pacchetto di supporto per importare le registrazioni in Blender.", "Há um pacote de ajuda nesta pasta para importar as gravações no Blender.", "このフォルダには録画をBlenderにインポートするためのヘルパーパッケージがあります。", "В этой папке есть вспомогательный пакет для импорта записей в Blender.");
            R("Mocap.Recording", "Enregistrement", "Recording", "Aufnahme", "Grabando", "Registrazione", "Gravando", "録画中", "Запись");
            R("Mocap.Stopped", "Arrêté", "Stopped", "Gestoppt", "Detenido", "Fermato", "Parado", "停止中", "Остановлено");
            R("Mocap.Samples", "échantillons", "samples", "Samples", "muestras", "campioni", "amostras", "サンプル", "образцов");
            R("Mocap.StartRecord", "Démarrer l'enregistrement", "Start recording", "Aufnahme starten", "Iniciar grabación", "Avvia registrazione", "Iniciar gravação", "録画開始", "Начать запись");
            R("Mocap.StopSave", "Arrêter et sauvegarder", "Stop and save", "Stoppen und speichern", "Detener y guardar", "Ferma e salva", "Parar e salvar", "停止して保存", "Остановить и сохранить");
            R("Mocap.Saving", "Sauvegarde en cours...", "Saving...", "Speichern...", "Guardando...", "Salvataggio in corso...", "Salvando...", "保存中...", "Сохранение...");
            R("Mocap.PlayerFPS", "FPS joueurs", "Player FPS", "Spieler-FPS", "FPS de jugadores", "FPS giocatori", "FPS de jogadores", "プレイヤーFPS", "FPS игроков");
            R("Mocap.ToRecord", "À enregistrer", "To record", "Aufzunehmen", "Para grabar", "Da registrare", "Para gravar", "録画対象", "Что записывать");
            R("Mocap.CheckPlayers", "Vérifier les joueurs", "Check players", "Spieler prüfen", "Comprobar jugadores", "Controlla giocatori", "Verificar jogadores", "プレイヤーを確認", "Проверить игроков");
            R("Mocap.NoPlayers", "Aucun joueur détecté.", "No players detected.", "Keine Spieler erkannt.", "No se detectaron jugadores.", "Nessun giocatore rilevato.", "Nenhum jogador detectado.", "プレイヤーが検出されません。", "Игроки не обнаружены.");
            R("Mocap.PlayersFound", "joueur(s) trouvé(s)", "player(s) found", "Spieler gefunden", "jugador(es) encontrado(s)", "giocatore/i trovato/i", "jogador(es) encontrado(s)", "人のプレイヤーが見つかりました", "игрок(ов) найдено");
            R("Mocap.SpectatorCam", "Caméra spectateur", "Spectator camera", "Zuschauerkamera", "Cámara de espectador", "Fotocamera spettatore", "Câmera de espectador", "観戦カメラ", "Камера зрителя");
            R("Mocap.RefreshCam", "Actualiser la caméra", "Refresh camera", "Kamera aktualisieren", "Actualizar cámara", "Aggiorna fotocamera", "Atualizar câmera", "カメラを更新", "Обновить камеру");
            R("Mocap.AlignToMe", "Aligner sur moi", "Align to me", "Auf mich ausrichten", "Alinear a mí", "Allinea a me", "Alinhar a mim", "自分に合わせる", "Выровнять по мне");
            R("Mocap.CamFPS", "FPS caméra", "Camera FPS", "Kamera-FPS", "FPS de cámara", "FPS fotocamera", "FPS da câmera", "カメラFPS", "FPS камеры");
            R("Mocap.CamHint", "La caméra spectateur doit être active dans la salle pour être enregistrée.", "The spectator camera must be active in the room to be recorded.", "Die Zuschauerkamera muss im Raum aktiv sein, um aufgenommen zu werden.", "La cámara de espectador debe estar activa en la sala para poder grabarse.", "La fotocamera spettatore deve essere attiva nella stanza per essere registrata.", "A câmera de espectador deve estar ativa na sala para ser gravada.", "録画するには観戦カメラがルーム内でアクティブである必要があります。", "Камера зрителя должна быть активна в комнате, чтобы записываться.");
            R("Mocap.AlignHint", "Positionne temporairement la caméra sur ta tête, puis la restaure après 1.5s.", "Temporarily positions the camera on your head, then restores it after 1.5s.", "Positioniert die Kamera vorübergehend auf deinem Kopf und stellt sie nach 1,5s wieder her.", "Coloca temporalmente la cámara en tu cabeza y la restaura después de 1.5s.", "Posiziona temporaneamente la fotocamera sulla tua testa, poi la ripristina dopo 1.5s.", "Posiciona temporariamente a câmera na sua cabeça e a restaura após 1.5s.", "一時的にカメラを頭の位置に移動し、1.5秒後に元に戻します。", "Временно позиционирует камеру на твоей голове, затем восстанавливает через 1.5с.");
            R("Mocap.CamFound", "Trouvée", "Found", "Gefunden", "Encontrada", "Trovata", "Encontrada", "見つかりました", "Найдена");
            R("Mocap.CamNotFound", "Non trouvée", "Not found", "Nicht gefunden", "No encontrada", "Non trovata", "Não encontrada", "見つかりません", "Не найдена");
            R("Mocap.CamLost", "Caméra perdue, enregistrement arrêté.", "Camera lost, recording stopped.", "Kamera verloren, Aufnahme gestoppt.", "Cámara perdida, grabación detenida.", "Fotocamera persa, registrazione interrotta.", "Câmera perdida, gravação interrompida.", "カメラが見つからないため、録画を停止しました。", "Камера потеряна, запись остановлена.");
            R("Mocap.CamSnapped", "Caméra alignée sur ta position.", "Camera snapped to your position.", "Kamera auf deine Position ausgerichtet.", "Cámara alineada a tu posición.", "Fotocamera allineata alla tua posizione.", "Câmera alinhada à sua posição.", "カメラをあなたの位置に合わせました。", "Камера выровнена по твоей позиции.");
            R("Mocap.CamRestored", "Caméra restaurée à sa position d'origine.", "Camera restored to its original position.", "Kamera auf ursprüngliche Position zurückgesetzt.", "Cámara restaurada a su posición original.", "Fotocamera ripristinata alla posizione originale.", "Câmera restaurada à posição original.", "カメラを元の位置に戻しました。", "Камера восстановлена в исходное положение.");
            R("Mocap.CantSnapRecording", "Impossible d'aligner la caméra pendant l'enregistrement.", "Can't snap the camera while recording.", "Kamera kann während der Aufnahme nicht ausgerichtet werden.", "No se puede alinear la cámara mientras se graba.", "Impossibile allineare la fotocamera durante la registrazione.", "Não é possível alinhar a câmera durante a gravação.", "録画中はカメラを合わせることができません。", "Нельзя выровнять камеру во время записи.");
            R("Mocap.CamExported", "Caméra exportée", "Camera exported", "Kamera exportiert", "Cámara exportada", "Fotocamera esportata", "Câmera exportada", "カメラをエクスポートしました", "Камера экспортирована");
            R("Mocap.Origin", "Origine", "Origin", "Ursprung", "Origen", "Origine", "Origem", "原点", "Начало координат");
            R("Mocap.OriginSet", "Origine définie", "Origin set", "Ursprung festgelegt", "Origen definido", "Origine impostata", "Origem definida", "原点設定済み", "Начало координат установлено");
            R("Mocap.OriginNotSet", "Origine non définie", "Origin not set", "Ursprung nicht festgelegt", "Origen no definido", "Origine non impostata", "Origem não definida", "原点未設定", "Начало координат не установлено");
            R("Mocap.OriginCleared", "Origine réinitialisée.", "Origin cleared.", "Ursprung zurückgesetzt.", "Origen restablecido.", "Origine azzerata.", "Origem redefinida.", "原点をリセットしました。", "Начало координат сброшено.");
            R("Mocap.OriginAuto", "L'origine est calibrée automatiquement à ta position au démarrage.", "The origin is automatically calibrated to your position on start.", "Der Ursprung wird beim Start automatisch auf deine Position kalibriert.", "El origen se calibra automáticamente en tu posición al inicio.", "L'origine viene calibrata automaticamente sulla tua posizione all'avvio.", "A origem é calibrada automaticamente na sua posição ao iniciar.", "起動時に原点は自動的にあなたの位置に校正されます。", "Начало координат автоматически калибруется по твоей позиции при запуске.");
            R("Mocap.OriginGizmo", "Un repère 3D (X rouge, Y vert, Z bleu) marque l'origine dans le jeu.", "A 3D gizmo (X red, Y green, Z blue) marks the origin in-game.", "Ein 3D-Gizmo (X rot, Y grün, Z blau) markiert den Ursprung im Spiel.", "Un gizmo 3D (X rojo, Y verde, Z azul) marca el origen en el juego.", "Un gizmo 3D (X rosso, Y verde, Z blu) segna l'origine nel gioco.", "Um gizmo 3D (X vermelho, Y verde, Z azul) marca a origem no jogo.", "3Dギズモ（X赤、Y緑、Z青）がゲーム内で原点を示します。", "3D-гизмо (X красный, Y зелёный, Z синий) отмечает начало координат в игре.");
            R("Mocap.ClearOrigin", "Effacer l'origine", "Clear origin", "Ursprung löschen", "Borrar origen", "Cancella origine", "Limpar origem", "原点をクリア", "Очистить начало координат");
            R("Mocap.NoOrigin", "Définis d'abord une origine.", "Set an origin first.", "Lege zuerst einen Ursprung fest.", "Define primero un origen.", "Imposta prima un'origine.", "Defina primeiro uma origem.", "先に原点を設定してください。", "Сначала установи начало координат.");
            R("Mocap.SelectPlayerOrCam", "Sélectionne au moins un joueur ou la caméra.", "Select at least one player or the camera.", "Wähle mindestens einen Spieler oder die Kamera aus.", "Selecciona al menos un jugador o la cámara.", "Seleziona almeno un giocatore o la fotocamera.", "Selecione pelo menos um jogador ou a câmera.", "少なくとも1人のプレイヤーかカメラを選択してください。", "Выбери хотя бы одного игрока или камеру.");
            R("Mocap.PlayerNotFound", "Joueur introuvable.", "Player not found.", "Spieler nicht gefunden.", "Jugador no encontrado.", "Giocatore non trovato.", "Jogador não encontrado.", "プレイヤーが見つかりません。", "Игрок не найден.");
            R("Mocap.NothingToSave", "Rien à sauvegarder.", "Nothing to save.", "Nichts zu speichern.", "Nada que guardar.", "Niente da salvare.", "Nada para salvar.", "保存するものがありません。", "Нечего сохранять.");
            R("Mocap.FilesExported", "fichier(s) exporté(s)", "file(s) exported", "Datei(en) exportiert", "archivo(s) exportado(s)", "file esportato/i", "arquivo(s) exportado(s)", "個のファイルをエクスポートしました", "файл(ов) экспортировано");
            R("Mocap.LastExported", "Derniers fichiers exportés", "Last exported files", "Zuletzt exportierte Dateien", "Últimos archivos exportados", "Ultimi file esportati", "Últimos arquivos exportados", "最後にエクスポートしたファイル", "Последние экспортированные файлы");
            R("Mocap.Folder", "Dossier", "Folder", "Ordner", "Carpeta", "Cartella", "Pasta", "フォルダ", "Папка");

            R("Rooms.Title", "Salles", "Rooms", "Räume", "Salas", "Stanze", "Salas", "ルーム", "Комнаты");
            R("Rooms.Current", "Salle actuelle", "Current room", "Aktueller Raum", "Sala actual", "Stanza attuale", "Sala atual", "現在のルーム", "Текущая комната");
            R("Rooms.Menu", "Menu principal", "Main menu", "Hauptmenü", "Menú principal", "Menu principale", "Menu principal", "メインメニュー", "Главное меню");
            R("Rooms.Leave", "Quitter", "Leave", "Verlassen", "Salir", "Esci", "Sair", "退出", "Выйти");
            R("Rooms.Leaving", "Deconnexion...", "Leaving...", "Verlasse...", "Saliendo...", "Uscita...", "Saindo...", "退出中...", "Выход...");
            R("Rooms.Random", "Salle aléatoire", "Random room", "Zufälliger Raum", "Sala aleatoria", "Stanza casuale", "Sala aleatória", "ランダムルーム", "Случайная комната");
            R("Rooms.Prefix", "Préfixe :", "Prefix:", "Präfix:", "Prefijo:", "Prefisso:", "Prefixo:", "プレフィックス：", "Префикс:");
            R("Rooms.Generate", "Générer", "Generate", "Generieren", "Generar", "Genera", "Gerar", "生成", "Сгенерировать");
            R("Rooms.Example", "Ex: FR1203  EU5541  US9812", "Ex: FR1203  EU5541  US9812", "Bsp: FR1203  EU5541  US9812", "Ej: FR1203  EU5541  US9812", "Es: FR1203  EU5541  US9812", "Ex: FR1203  EU5541  US9812", "例：FR1203  EU5541  US9812", "Пример: FR1203  EU5541  US9812");
            R("Rooms.Join", "Rejoindre une salle", "Join a room", "Raum beitreten", "Unirse a una sala", "Unisciti a una stanza", "Entrar em uma sala", "ルームに参加", "Присоединиться к комнате");
            R("Rooms.JoinBtn", "Rejoindre", "Join", "Beitreten", "Unirse", "Unisciti", "Entrar", "参加", "Присоединиться");
            R("Rooms.Joining", "Connexion...", "Joining...", "Verbinde...", "Uniendo...", "Unione...", "Entrando...", "参加中...", "Подключение...");
            R("Rooms.Favorite", "Favori", "Favorite", "Favorit", "Favorito", "Preferito", "Favorito", "お気に入り", "Избранное");
            R("Rooms.Favorites", "Favoris", "Favorites", "Favoriten", "Favoritos", "Preferiti", "Favoritos", "お気に入り", "Избранное");
            R("Rooms.NoFavorites", "Aucun favori.", "No favorites.", "Keine Favoriten.", "No hay favoritos.", "Nessun preferito.", "Sem favoritos.", "お気に入りがありません。", "Нет избранных.");
            R("Rooms.DisabledHint", "Active ce mod depuis la page Mods pour voir ses réglages.", "Enable this mod from the Mods page to see its settings.", "Aktiviere diesen Mod auf der Mods-Seite, um die Einstellungen zu sehen.", "Activa este mod desde la página de Mods para ver sus ajustes.", "Attiva questa mod dalla pagina Mod per vedere le impostazioni.", "Ative este mod na página de Mods para ver as configurações.", "設定を見るには、Modページからこのmodを有効にしてください。", "Включи этот мод на странице Модов, чтобы увидеть настройки.");

            R("Tracking.Title", "Tracking Corporel", "Body Tracking", "Körper-Tracking", "Seguimiento corporal", "Tracciamento del corpo", "Rastreamento corporal", "ボディトラッキング", "Отслеживание тела");
            R("Discord.Title", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence");
            R("Stream.Title", "Streamer Overlay", "Streamer Overlay", "Streamer-Overlay", "Superposición de streamer", "Sovrapposizione streamer", "Sobreposição de streamer", "ストリーマーオーバーレイ", "Оверлей стримера");
            R("Voice.Title", "Chat Vocal", "Voice Chat", "Sprachchat", "Chat de voz", "Chat vocale", "Chat de voz", "ボイスチャット", "Голосовой чат");
            R("Spawner.Title", "Objets 3D", "3D Objects", "3D-Objekte", "Objetos 3D", "Oggetti 3D", "Objetos 3D", "3Dオブジェクト", "3D-объекты");
            R("Names.Title", "Presets de Noms", "Name Presets", "Namensvoreinstellungen", "Ajustes preestablecidos de nombres", "Preset nomi", "Predefinições de nomes", "名前プリセット", "Пресеты имён");
            R("Miror.Title", "Miroir", "Mirror", "Spiegel", "Espejo", "Specchio", "Espelho", "鏡", "Зеркало");
            R("Face.Title", "Visage Personnalisé", "Custom Face", "Benutzerdefiniertes Gesicht", "Cara personalizada", "Viso personalizzato", "Rosto personalizado", "カスタムフェイス", "Пользовательское лицо");

            R("TimeWeather.Category", "Environnement", "Environment", "Umgebung", "Entorno", "Ambiente", "Ambiente", "環境", "Окружение");
            R("TimeWeather.Title", "Météo", "Weather", "Wetter", "Clima", "Meteo", "Clima", "天気", "Погода");
            R("TimeWeather.Section", "Météo", "Weather", "Wetter", "Clima", "Meteo", "Clima", "天気", "Погода");
            R("TimeWeather.NotFound", "BetterDayNightManager introuvable.", "BetterDayNightManager not found.", "BetterDayNightManager nicht gefunden.", "BetterDayNightManager no encontrado.", "BetterDayNightManager non trovato.", "BetterDayNightManager não encontrado.", "BetterDayNightManagerが見つかりません。", "BetterDayNightManager не найден.");
            R("TimeWeather.HourLabel", "Heure : ", "Time: ", "Uhrzeit: ", "Hora: ", "Ora: ", "Hora: ", "時間：", "Время: ");
            R("TimeWeather.WeatherLabel", "Météo", "Weather", "Wetter", "Clima", "Meteo", "Clima", "天気", "Погода");
            R("TimeWeather.CustomTimeLabel", "Temps personnalisé", "Custom time", "Benutzerdefinierte Zeit", "Hora personalizada", "Orario personalizzato", "Horário personalizado", "カスタム時間", "Своё время");
            R("TimeWeather.PickHour", "Choisir l'heure", "Pick a time", "Uhrzeit wählen", "Elegir hora", "Scegli l'ora", "Escolher horário", "時間を選択", "Выбрать время");
            R("TimeWeather.RainLabel", "Pluie", "Rain", "Regen", "Lluvia", "Pioggia", "Chuva", "雨", "Дождь");
            R("TimeWeather.RainVolume", "Volume de la pluie", "Rain volume", "Regenlautstärke", "Volumen de lluvia", "Volume pioggia", "Volume da chuva", "雨の音量", "Громкость дождя");

            R("Voice.Section", "Chat Vocal", "Voice Chat", "Sprachchat", "Chat de voz", "Chat vocale", "Chat de voz", "ボイスチャット", "Голосовой чат");
            R("Voice.RadiusNear", "Rayon proche (volume normal)", "Near radius (normal volume)", "Nahbereich (normale Lautstärke)", "Radio cercano (volumen normal)", "Raggio vicino (volume normale)", "Raio próximo (volume normal)", "近距離 (通常音量)", "Ближний радиус (нормальная громкость)");
            R("Voice.RadiusFar", "Rayon lointain (volume réduit)", "Far radius (reduced volume)", "Fernbereich (reduzierte Lautstärke)", "Radio lejano (volumen reducido)", "Raggio lontano (volume ridotto)", "Raio distante (volume reduzido)", "遠距離 (音量低下)", "Дальний радиус (пониженная громкость)");
            R("Voice.FarVolume", "Volume au-delà du rayon lointain", "Volume beyond far radius", "Lautstärke jenseits des Fernbereichs", "Volumen más allá del radio lejano", "Volume oltre il raggio lontano", "Volume além do raio distante", "遠距離外の音量", "Громкость за дальним радиусом");
            R("Voice.Enable", "Activer", "Enable", "Aktivieren", "Activar", "Attiva", "Ativar", "有効化", "Включить");

            // --- Ajoutées : le toggle local a été retiré (état géré par la page Mods) et la
            // normalisation automatique a été supprimée au profit d'un aperçu visuel des rayons. ---
            R("Voice.EnableFromModsHint",
                "Active ce mod depuis la page Mods pour voir ses réglages.",
                "Enable this mod from the Mods page to see its settings.",
                "Aktiviere diesen Mod auf der Mods-Seite, um die Einstellungen zu sehen.",
                "Activa este mod desde la página de Mods para ver sus ajustes.",
                "Attiva questa mod dalla pagina Mod per vedere le impostazioni.",
                "Ative este mod na página de Mods para ver as configurações.",
                "設定を見るには、Modページからこのmodを有効にしてください。",
                "Включи этот мод на странице Модов, чтобы увидеть настройки.");
            R("Voice.PreviewHint",
                "Une bulle colorée autour de toi indique les rayons pendant 3s après un réglage.",
                "A colored bubble around you shows the radii for 3s after a change.",
                "Eine farbige Blase um dich zeigt 3s lang die Radien nach einer Änderung.",
                "Una burbuja de color a tu alrededor muestra los radios durante 3s tras un ajuste.",
                "Una bolla colorata intorno a te mostra i raggi per 3s dopo una modifica.",
                "Uma bolha colorida ao seu redor mostra os raios por 3s após um ajuste.",
                "設定変更後3秒間、周囲に色付きの球体で半径が表示されます。",
                "Цветной пузырь вокруг тебя показывает радиусы в течение 3с после изменения.");

            R("Tracking.Section", "Body Tracking", "Body Tracking", "Körper-Tracking", "Seguimiento corporal", "Tracciamento del corpo", "Rastreamento corporal", "ボディトラッキング", "Отслеживание тела");
            R("Tracking.CalibFailed", "Échec de la calibration (tracker introuvable).", "Calibration failed (tracker not found).", "Kalibrierung fehlgeschlagen (Tracker nicht gefunden).", "Calibración fallida (rastreador no encontrado).", "Calibrazione fallita (tracker non trovato).", "Falha na calibração (rastreador não encontrado).", "キャリブレーション失敗（トラッカーが見つかりません）。", "Ошибка калибровки (трекер не найден).");
            R("Tracking.ModeFangame", "Fangame", "Fangame", "Fangame", "Fangame", "Fangame", "Fangame", "ファンゲーム", "Фангейм");
            R("Tracking.ModeFangameDesc", "Style fangame classique : le buste suit la tête.", "Classic fangame style: the chest follows the head.", "Klassischer Fangame-Stil: Die Brust folgt dem Kopf.", "Estilo fangame clásico: el pecho sigue a la cabeza.", "Stile fangame classico: il petto segue la testa.", "Estilo fangame clássico: o peito segue a cabeça.", "クラシックなファンゲームスタイル：胸が頭に追従します。", "Классический стиль фангейма: грудь следует за головой.");
            R("Tracking.ModeSmooth", "Lissé", "Smooth", "Sanft", "Suave", "Fluido", "Suave", "スムーズ", "Плавный");
            R("Tracking.ModeSmoothDesc", "Ne suit que la rotation horizontale de la tête, en douceur.", "Only follows the head's horizontal rotation, smoothly.", "Folgt nur der horizontalen Kopfdrehung, sanft.", "Solo sigue la rotación horizontal de la cabeza, suavemente.", "Segue solo la rotazione orizzontale della testa, in modo fluido.", "Segue apenas a rotação horizontal da cabeça, suavemente.", "頭の水平回転のみをなめらかに追従します。", "Плавно следует только за горизонтальным поворотом головы.");
            R("Tracking.ModeTrackers", "Trackers SteamVR", "SteamVR trackers", "SteamVR-Tracker", "Rastreadores SteamVR", "Tracker SteamVR", "Rastreadores SteamVR", "SteamVRトラッカー", "Трекеры SteamVR");
            R("Tracking.ModeTrackersDesc", "Utilise un ou plusieurs trackers SteamVR pour un tracking précis du buste.", "Uses one or more SteamVR trackers for precise chest tracking.", "Verwendet einen oder mehrere SteamVR-Tracker für präzises Brust-Tracking.", "Usa uno o más rastreadores SteamVR para un seguimiento preciso del pecho.", "Usa uno o più tracker SteamVR per un tracciamento preciso del petto.", "Usa um ou mais rastreadores SteamVR para rastreamento preciso do peito.", "1つ以上のSteamVRトラッカーを使用して胸を正確にトラッキングします。", "Использует один или несколько трекеров SteamVR для точного отслеживания груди.");
            R("Tracking.ModeCustom", "Personnalisé", "Custom", "Benutzerdefiniert", "Personalizado", "Personalizzato", "Personalizado", "カスタム", "Пользовательский");
            R("Tracking.ModeCustomDesc", "Applique un décalage fixe (en degrés) par rapport à la rotation de la tête.", "Applies a fixed offset (in degrees) relative to head rotation.", "Wendet einen festen Versatz (in Grad) relativ zur Kopfdrehung an.", "Aplica un desplazamiento fijo (en grados) relativo a la rotación de la cabeza.", "Applica un offset fisso (in gradi) rispetto alla rotazione della testa.", "Aplica um deslocamento fixo (em graus) em relação à rotação da cabeça.", "頭の回転に対して固定オフセット（度）を適用します。", "Применяет фиксированное смещение (в градусах) относительно вращения головы.");
            R("Tracking.ModeTrackersShort", "Trackers", "Trackers", "Tracker", "Rastread.", "Tracker", "Rastread.", "トラッカー", "Трекеры");
            R("Tracking.ModeCustomShort", "Custom", "Custom", "Custom", "Custom", "Custom", "Custom", "カスタム", "Кастом");
            R("Tracking.Active", "ACTIF", "ACTIVE", "AKTIV", "ACTIVO", "ATTIVO", "ATIVO", "アクティブ", "АКТИВНО");
            R("Tracking.Inactive", "INACTIF", "INACTIVE", "INAKTIV", "INACTIVO", "INATTIVO", "INATIVO", "非アクティブ", "НЕАКТИВНО");
            R("Tracking.Enable", "Activer", "Enable", "Aktivieren", "Activar", "Attiva", "Ativar", "有効化", "Включить");
            R("Tracking.Disable", "Désactiver", "Disable", "Deaktivieren", "Desactivar", "Disattiva", "Desativar", "無効化", "Выключить");
            R("Tracking.Mode", "Mode", "Mode", "Modus", "Modo", "Modalità", "Modo", "モード", "Режим");
            R("Tracking.Smooth", "Lissage", "Smoothing", "Glättung", "Suavizado", "Smussatura", "Suavização", "スムージング", "Сглаживание");
            R("Tracking.Trackers", "Trackers", "Trackers", "Tracker", "Rastreadores", "Tracker", "Rastreadores", "トラッカー", "Трекеры");
            R("Tracking.Custom", "Personnalisé", "Custom", "Benutzerdefiniert", "Personalizado", "Personalizzato", "Personalizado", "カスタム", "Пользовательский");
            R("Tracking.Detected", "Trackers détectés", "Detected trackers", "Erkannte Tracker", "Rastreadores detectados", "Tracker rilevati", "Rastreadores detectados", "検出されたトラッカー", "Обнаруженные трекеры");
            R("Tracking.Chest", "Chest :", "Chest:", "Brust:", "Pecho:", "Petto:", "Peito:", "胸：", "Грудь:");
            R("Tracking.NotDetected", "Non détecté", "Not detected", "Nicht erkannt", "No detectado", "Non rilevato", "Não detectado", "未検出", "Не обнаружен");
            R("Tracking.ActiveDevices", "Actifs :", "Active:", "Aktiv:", "Activos:", "Attivi:", "Ativos:", "アクティブ：", "Активные:");
            R("Tracking.Reassign", "Ré-assigner", "Re-assign", "Neu zuweisen", "Reasignar", "Riassegna", "Reatribuir", "再割り当て", "Переназначить");
            R("Tracking.Calibration", "Calibration", "Calibration", "Kalibrierung", "Calibración", "Calibrazione", "Calibração", "キャリブレーション", "Калибровка");
            R("Tracking.Calibrated", "Calibré", "Calibrated", "Kalibriert", "Calibrado", "Calibrato", "Calibrado", "キャリブレーション済み", "Откалибровано");
            R("Tracking.NotCalibrated", "Non calibré", "Not calibrated", "Nicht kalibriert", "No calibrado", "Non calibrato", "Não calibrado", "未キャリブレーション", "Не откалибровано");
            R("Tracking.CalibrateBtn", "Calibrer (3-2-1)", "Calibrate (3-2-1)", "Kalibrieren (3-2-1)", "Calibrar (3-2-1)", "Calibra (3-2-1)", "Calibrar (3-2-1)", "キャリブレーション (3-2-1)", "Калибровка (3-2-1)");
            R("Tracking.ResetCalib", "Réinitialiser calib", "Reset calibration", "Kalibrierung zurücksetzen", "Restablecer calibración", "Reimposta calibrazione", "Redefinir calibração", "キャリブレーションリセット", "Сброс калибровки");
            R("Tracking.CalibrateHint", "Mets-toi droit face devant puis appuie", "Stand straight facing forward then press", "Stell dich gerade nach vorne gerichtet hin und drücke", "Ponte derecho mirando al frente y presiona", "Mettiti dritto rivolto in avanti e premi", "Fique em pé de frente e pressione", "まっすぐ前を向いて立って押してください", "Встань прямо лицом вперёд и нажми");
            R("Tracking.Offset", "Offset tracker (ajustement manuel)", "Tracker offset (manual adjustment)", "Tracker-Offset (manuelle Anpassung)", "Offset del rastreador (ajuste manual)", "Offset tracker (regolazione manuale)", "Offset do rastreador (ajuste manual)", "トラッカーオフセット (手動調整)", "Смещение трекера (ручная настройка)");
            R("Tracking.OffsetX", "Offset X", "Offset X", "Offset X", "Offset X", "Offset X", "Offset X", "Xオフセット", "Смещение X");
            R("Tracking.OffsetY", "Offset Y", "Offset Y", "Offset Y", "Offset Y", "Offset Y", "Offset Y", "Yオフセット", "Смещение Y");
            R("Tracking.OffsetZ", "Offset Z", "Offset Z", "Offset Z", "Offset Z", "Offset Z", "Offset Z", "Zオフセット", "Смещение Z");
            R("Tracking.ResetOffset", "Réinitialiser offset (0 / 0 / 0)", "Reset offset (0 / 0 / 0)", "Offset zurücksetzen (0 / 0 / 0)", "Restablecer offset (0 / 0 / 0)", "Reimposta offset (0 / 0 / 0)", "Redefinir offset (0 / 0 / 0)", "オフセットリセット (0 / 0 / 0)", "Сброс смещения (0 / 0 / 0)");
            R("Tracking.CustomOffset", "Offset personnalisé (ajouté à la tête)", "Custom offset (added to head)", "Benutzerdefinierter Offset (zum Kopf hinzugefügt)", "Offset personalizado (añadido a la cabeza)", "Offset personalizzato (aggiunto alla testa)", "Offset personalizado (adicionado à cabeça)", "カスタムオフセット (頭に追加)", "Пользовательское смещение (добавляется к голове)");

            R("Discord.Section", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence", "Discord Rich Presence");
            R("Discord.Status", "Statut", "Status", "Status", "Estado", "Stato", "Status", "ステータス", "Статус");
            R("Discord.Connected", "Connecté !", "Connected!", "Verbunden!", "¡Conectado!", "Connesso!", "Conectado!", "接続済み！", "Подключено!");
            R("Discord.Disconnected", "Déconnecté", "Disconnected", "Getrennt", "Desconectado", "Disconnesso", "Desconectado", "切断済み", "Отключено");
            R("Discord.NotConnected", "Non connecté", "Not connected", "Nicht verbunden", "No conectado", "Non connesso", "Não conectado", "未接続", "Не подключено");
            R("Discord.Connecting", "Connexion...", "Connecting...", "Verbindung wird hergestellt...", "Conectando...", "Connessione...", "Conectando...", "接続中...", "Подключение...");
            R("Discord.PipeNotFound", "Pipe introuvable - Discord est-il ouvert ?", "Pipe not found - is Discord open?", "Pipe nicht gefunden - ist Discord geöffnet?", "No se encontró el pipe - ¿está Discord abierto?", "Pipe non trovato - Discord è aperto?", "Pipe não encontrado - o Discord está aberto?", "パイプが見つかりません - Discordは起動していますか？", "Канал не найден - Discord открыт?");
            R("Discord.InvalidAppID", "App ID invalide", "Invalid App ID", "Ungültige App-ID", "ID de aplicación inválida", "ID app non valido", "ID do aplicativo inválido", "無効なアプリID", "Неверный ID приложения");
            R("Discord.ConnectedEmptyResponse", "Connecté (réponse vide)", "Connected (empty response)", "Verbunden (leere Antwort)", "Conectado (respuesta vacía)", "Connesso (risposta vuota)", "Conectado (resposta vazia)", "接続済み（空の応答）", "Подключено (пустой ответ)");
            R("Discord.ConnectedTimeout", "Connecté (délai dépassé)", "Connected (timeout)", "Verbunden (Zeitüberschreitung)", "Conectado (tiempo agotado)", "Connesso (timeout)", "Conectado (tempo esgotado)", "接続済み（タイムアウト）", "Подключено (тайм-аут)");
            R("Discord.Error", "Erreur", "Error", "Fehler", "Error", "Errore", "Erro", "エラー", "Ошибка");
            R("Discord.EmptyAppIDHint", "(vide - clique sur Afficher)", "(empty - click Show)", "(leer - klicke auf Anzeigen)", "(vacío - haz clic en Mostrar)", "(vuoto - clicca su Mostra)", "(vazio - clique em Mostrar)", "（空 - 「表示」をクリック）", "(пусто - нажми Показать)");
            R("Discord.Room", "Salle", "Room", "Raum", "Sala", "Stanza", "Sala", "ルーム", "Комната");
            R("Discord.Players", "Joueurs", "Players", "Spieler", "Jugadores", "Giocatori", "Jogadores", "プレイヤー", "Игроки");
            R("Discord.AppID", "Identifiant d'application :", "Application ID:", "Anwendungs-ID:", "ID de aplicación:", "ID applicazione:", "ID do aplicativo:", "アプリケーションID：", "ID приложения:");
            R("Discord.Show", "Afficher", "Show", "Anzeigen", "Mostrar", "Mostra", "Mostrar", "表示", "Показать");
            R("Discord.Hide", "Cacher", "Hide", "Ausblenden", "Ocultar", "Nascondi", "Ocultar", "隠す", "Скрыть");
            R("Discord.SaveConnect", "Sauvegarder et connecter", "Save and connect", "Speichern und verbinden", "Guardar y conectar", "Salva e connetti", "Salvar e conectar", "保存して接続", "Сохранить и подключить");
            R("Discord.ForceUpdate", "Forcer la mise à jour", "Force update", "Update erzwingen", "Forzar actualización", "Forza aggiornamento", "Forçar atualização", "強制更新", "Принудительное обновление");
            R("Discord.DisconnectBtn", "Déconnecter", "Disconnect", "Trennen", "Desconectar", "Disconnetti", "Desconectar", "切断", "Отключить");
            R("Discord.HowTo", "Comment faire :", "How to:", "Wie geht das:", "Cómo hacer:", "Come fare:", "Como fazer:", "やり方：", "Как сделать:");
            R("Discord.Step1", "1. discord.com/developers/applications", "1. discord.com/developers/applications", "1. discord.com/developers/applications", "1. discord.com/developers/applications", "1. discord.com/developers/applications", "1. discord.com/developers/applications", "1. discord.com/developers/applications", "1. discord.com/developers/applications");
            R("Discord.Step2", "2. New Application > donne un nom", "2. New Application > give it a name", "2. Neue Anwendung > gib einen Namen ein", "2. Nueva aplicación > ponle un nombre", "2. Nuova applicazione > dagli un nome", "2. Novo aplicativo > dê um nome", "2. 新しいアプリケーション > 名前を付ける", "2. Новое приложение > дайте имя");
            R("Discord.Step3", "3. Copie l'Application ID", "3. Copy the Application ID", "3. Kopiere die Anwendungs-ID", "3. Copia el ID de aplicación", "3. Copia l'ID applicazione", "3. Copie o ID do aplicativo", "3. アプリケーションIDをコピー", "3. Скопируй ID приложения");
            R("Discord.Step4", "4. Colle-le ici et clique Connecter", "4. Paste it here and click Connect", "4. Füge ihn hier ein und klicke auf Verbinden", "4. Pégalo aquí y haz clic en Conectar", "4. Incollalo qui e clicca Connetti", "4. Cole aqui e clique em Conectar", "4. ここに貼り付けて「接続」をクリック", "4. Вставь сюда и нажми «Подключить»");
            R("Discord.Step5", "5. Discord doit être ouvert !", "5. Discord must be open!", "5. Discord muss geöffnet sein!", "5. ¡Discord debe estar abierto!", "5. Discord deve essere aperto!", "5. O Discord deve estar aberto!", "5. Discordを開いておく必要があります！", "5. Discord должен быть открыт!");

            R("Stream.Section", "Streamer Overlay", "Streamer Overlay", "Streamer-Overlay", "Superposición de streamer", "Sovrapposizione streamer", "Sobreposição de streamer", "ストリーマーオーバーレイ", "Оверлей стримера");
            R("Stream.Active", "Actif", "Active", "Aktiv", "Activo", "Attivo", "Ativo", "アクティブ", "Активно");
            R("Stream.Inactive", "Inactif", "Inactive", "Inaktiv", "Inactivo", "Inattivo", "Inativo", "非アクティブ", "Неактивно");
            R("Stream.Enable", "Activer", "Enable", "Aktivieren", "Activar", "Attiva", "Ativar", "有効化", "Включить");
            R("Stream.Disable", "Désactiver", "Disable", "Deaktivieren", "Desactivar", "Disattiva", "Desativar", "無効化", "Выключить");
            R("Stream.Show", "Afficher", "Show", "Anzeigen", "Mostrar", "Mostra", "Mostrar", "表示", "Показать");
            R("Stream.Hide", "Cacher", "Hide", "Ausblenden", "Ocultar", "Nascondi", "Ocultar", "隠す", "Скрыть");
            R("Stream.Dimensions", "Dimensions :", "Dimensions:", "Abmessungen:", "Dimensiones:", "Dimensioni:", "Dimensões:", "寸法：", "Размеры:");
            R("Stream.Width", "Largeur", "Width", "Breite", "Ancho", "Larghezza", "Largura", "幅", "Ширина");
            R("Stream.Height", "Hauteur", "Height", "Höhe", "Altura", "Altezza", "Altura", "高さ", "Высота");
            R("Stream.FontSize", "Police", "Font size", "Schriftgröße", "Tamaño de fuente", "Dimensione carattere", "Tamanho da fonte", "フォントサイズ", "Размер шрифта");
            R("Stream.TextColor", "Couleur du texte", "Text color", "Textfarbe", "Color del texto", "Colore del testo", "Cor do texto", "テキストの色", "Цвет текста");
            R("Stream.Room", "Salle :", "Room:", "Raum:", "Sala:", "Stanza:", "Sala:", "ルーム：", "Комната:");
            R("Stream.Players", "Joueurs :", "Players:", "Spieler:", "Jugadores:", "Giocatori:", "Jogadores:", "プレイヤー：", "Игроки:");
            R("Stream.Hidden", "Infos cachées", "Hidden info", "Ausgeblendete Informationen", "Información oculta", "Informazioni nascoste", "Informações ocultas", "隠し情報", "Скрытая информация");
            R("Stream.ObsExport", "Export pour OBS", "OBS export", "OBS-Export", "Exportar para OBS", "Esportazione OBS", "Exportar para OBS", "OBS用エクスポート", "Экспорт для OBS");
            R("Stream.ObsExportHint", "Ajoute une Text Source dans OBS et lie-la à ce fichier.", "Add a Text Source in OBS and link it to this file.", "Füge eine Textquelle in OBS hinzu und verknüpfe sie mit dieser Datei.", "Añade una fuente de texto en OBS y vincúlala a este archivo.", "Aggiungi una sorgente di testo in OBS e collegala a questo file.", "Adicione uma Fonte de Texto no OBS e vincule a este arquivo.", "OBSにテキストソースを追加し、このファイルにリンクしてください。", "Добавь текстовый источник в OBS и свяжи его с этим файлом.");
            R("Stream.EnableFromModsHint", "Active ce mod depuis la page Mods pour voir ses réglages.", "Enable this mod from the Mods page to see its settings.", "Aktiviere diesen Mod auf der Mods-Seite, um die Einstellungen zu sehen.", "Activa este mod desde la página de Mods para ver sus ajustes.", "Attiva questa mod dalla pagina Mod per vedere le impostazioni.", "Ative este mod na página de Mods para ver as configurações.", "設定を見るには、Modページからこのmodを有効にしてください。", "Включи этот мод на странице Модов, чтобы увидеть настройки.");
            R("Stream.BgToggle", "Fond de l'overlay", "Overlay background", "Overlay-Hintergrund", "Fondo del overlay", "Sfondo overlay", "Fundo do overlay", "オーバーレイの背景", "Фон оверлея");
            R("Stream.BgHint", "Désactive pour un fond transparent (utile avec une capture OBS avec alpha).", "Disable for a transparent background (useful with an alpha-aware OBS capture).", "Deaktivieren für transparenten Hintergrund (nützlich mit Alpha-fähiger OBS-Aufnahme).", "Desactiva para un fondo transparente (útil con una captura OBS con alfa).", "Disattiva per uno sfondo trasparente (utile con una cattura OBS con alfa).", "Desative para um fundo transparente (útil com captura OBS com alfa).", "透明な背景にするには無効化してください（アルファ対応のOBSキャプチャで便利）。", "Отключи для прозрачного фона (полезно с альфа-захватом OBS).");
            R("Stream.BgColor", "Couleur du fond", "Background color", "Hintergrundfarbe", "Color de fondo", "Colore sfondo", "Cor de fundo", "背景色", "Цвет фона");
            R("Stream.BgOpacity", "Opacité du fond", "Background opacity", "Hintergrundtransparenz", "Opacidad del fondo", "Opacità sfondo", "Opacidade do fundo", "背景の不透明度", "Прозрачность фона");
            R("Stream.ShadowToggle", "Ombre du texte", "Text shadow", "Textschatten", "Sombra del texto", "Ombra del testo", "Sombra do texto", "テキストの影", "Тень текста");
            R("Stream.ShadowHint", "Garde le texte lisible même sans fond ou sur un fond clair.", "Keeps text readable even without a background or on a light background.", "Hält den Text auch ohne Hintergrund oder auf hellem Hintergrund lesbar.", "Mantiene el texto legible incluso sin fondo o sobre un fondo claro.", "Mantiene il testo leggibile anche senza sfondo o su uno sfondo chiaro.", "Mantém o texto legível mesmo sem fundo ou em fundo claro.", "背景がない場合や明るい背景でもテキストを読みやすく保ちます。", "Сохраняет текст читаемым даже без фона или на светлом фоне.");
            R("Stream.ShadowColor", "Couleur de l'ombre", "Shadow color", "Schattenfarbe", "Color de la sombra", "Colore ombra", "Cor da sombra", "影の色", "Цвет тени");

            R("Spawner.Files", "Fichiers", "Files", "Dateien", "Archivos", "File", "Arquivos", "ファイル", "Файлы");
            R("Spawner.Found", "trouvé(s)", "found", "gefunden", "encontrado(s)", "trovato(i)", "encontrado(s)", "件見つかりました", "найдено");
            R("Spawner.Search", "Recherche :", "Search:", "Suche:", "Buscar:", "Cerca:", "Buscar:", "検索：", "Поиск:");
            R("Spawner.Refresh", "Actualiser", "Refresh", "Aktualisieren", "Actualizar", "Aggiorna", "Atualizar", "更新", "Обновить");
            R("Spawner.AutoBundleHelp", "Créer tes propres bundles", "Create your own bundles", "Erstelle deine eigenen Bundles", "Crea tus propios bundles", "Crea i tuoi bundle", "Crie seus próprios bundles", "自分のバンドルを作成", "Создай свои собственные бандлы");
            R("Spawner.AutoBundleHint", "Un package Unity d'aide se trouve dans ce dossier pour t'aider à créer des .bundle compatibles.", "A helper Unity package is in this folder to help you create compatible .bundle files.", "In diesem Ordner befindet sich ein Unity-Hilfspaket, mit dem du kompatible .bundle-Dateien erstellen kannst.", "Hay un paquete de Unity de ayuda en esta carpeta para crear archivos .bundle compatibles.", "In questa cartella è presente un pacchetto Unity di supporto per creare file .bundle compatibili.", "Há um pacote Unity de ajuda nesta pasta para criar arquivos .bundle compatíveis.", "このフォルダには互換性のある.bundleファイルを作成するためのUnityヘルパーパッケージがあります。", "В этой папке есть вспомогательный пакет Unity для создания совместимых файлов .bundle.");
            R("Spawner.NoFiles", "Aucun fichier .bundle trouvé.", "No .bundle files found.", "Keine .bundle-Dateien gefunden.", "No se encontraron archivos .bundle.", "Nessun file .bundle trovato.", "Nenhum arquivo .bundle encontrado.", ".bundleファイルが見つかりません。", "Файлы .bundle не найдены.");
            R("Spawner.SelectFile", "Sélectionne un fichier", "Select a file", "Wähle eine Datei", "Selecciona un archivo", "Seleziona un file", "Selecione um arquivo", "ファイルを選択", "Выберите файл");
            R("Spawner.Spawn", "Spawn devant moi", "Spawn in front of me", "Vor mir spawnen", "Aparecer delante de mí", "Spawna davanti a me", "Spawnar na minha frente", "目の前にスポーン", "Спавнить передо мной");
            R("Spawner.Objects", "Objets en jeu", "Objects in game", "Objekte im Spiel", "Objetos en juego", "Oggetti in gioco", "Objetos no jogo", "ゲーム内オブジェクト", "Объекты в игре");
            R("Spawner.Held", "tenu(s)", "held", "gehalten", "sostenido(s)", "tenuti", "segurado(s)", "保持中", "удерживаемых");
            R("Spawner.Free", "Libre", "Free", "Frei", "Libre", "Libero", "Livre", "自由", "Свободно");
            R("Spawner.HeldBy", "Tenu", "Held", "Gehalten", "Sostenido", "Tenuto", "Segurado", "保持中", "Удерживается");
            R("Spawner.Frozen", "Figé", "Frozen", "Gefroren", "Congelado", "Frozen", "Congelado", "凍結", "Заморожено");
            R("Spawner.Size", "Taille", "Size", "Größe", "Tamaño", "Dimensione", "Tamanho", "サイズ", "Размер");
            R("Spawner.Respawn", "Réapparaître", "Respawn", "Respawnen", "Reaparecer", "Respawna", "Respawne", "リスポーン", "Респавн");
            R("Spawner.Delete", "Supprimer", "Delete", "Löschen", "Eliminar", "Elimina", "Excluir", "削除", "Удалить");
            R("Spawner.NoObjects", "Aucun objet créé.", "No objects created.", "Keine Objekte erstellt.", "No se crearon objetos.", "Nessun oggetto creato.", "Nenhum objeto criado.", "オブジェクトが作成されていません。", "Объекты не созданы.");
            R("Spawner.VRWarning", "Mains VR non détectées, le grab ne fonctionnera pas.", "VR hands not detected, grab will not work.", "VR-Hände nicht erkannt, Greifen funktioniert nicht.", "Manos VR no detectadas, el agarre no funcionará.", "Mani VR non rilevate, il grab non funzionerà.", "Mãos VR não detectadas, o grab não funcionará.", "VRハンドが検出されません、グリップは機能しません。", "VR-руки не обнаружены, захват не будет работать.");

            R("Filter.Title", "Filtre d'écran", "Screen Filter", "Bildschirmfilter", "Filtro de pantalla", "Filtro schermo", "Filtro de tela", "画面フィルター", "Экранный фильтр");
            R("Filter.Section", "Réglages du filtre", "Filter Settings", "Filtereinstellungen", "Ajustes del filtro", "Impostazioni filtro", "Configurações do filtro", "フィルター設定", "Настройки фильтра");
            R("Filter.Presets", "Préréglages", "Presets", "Voreinstellungen", "Preajustes", "Preset", "Predefinições", "プリセット", "Пресеты");
            R("Filter.PresetWarm", "Chaud", "Warm", "Warm", "Cálido", "Caldo", "Quente", "ウォーム", "Тёплый");
            R("Filter.PresetCold", "Froid", "Cold", "Kalt", "Frío", "Freddo", "Frio", "クール", "Холодный");
            R("Filter.PresetCinematic", "Cinématique", "Cinematic", "Filmisch", "Cinemático", "Cinematico", "Cinemático", "シネマティック", "Кинематографичный");
            R("Filter.PresetVintage", "Vintage", "Vintage", "Vintage", "Vintage", "Vintage", "Vintage", "ヴィンテージ", "Винтаж");
            R("Filter.Reset", "Réinitialiser", "Reset", "Zurücksetzen", "Restablecer", "Reimposta", "Redefinir", "リセット", "Сброс");
            R("Filter.CustomPresets", "Mes préréglages", "My Presets", "Meine Voreinstellungen", "Mis preajustes", "I miei preset", "Minhas predefinições", "マイプリセット", "Мои пресеты");
            R("Filter.SaveAs", "Enregistrer", "Save", "Speichern", "Guardar", "Salva", "Salvar", "保存", "Сохранить");
            R("Filter.NoCustomPresets", "Aucun préréglage enregistré.", "No saved presets.", "Keine gespeicherten Voreinstellungen.", "No hay preajustes guardados.", "Nessun preset salvato.", "Nenhuma predefinição salva.", "保存されたプリセットはありません。", "Нет сохранённых пресетов.");
            R("Filter.Load", "Charger", "Load", "Laden", "Cargar", "Carica", "Carregar", "読込", "Загрузить");
            R("Filter.Delete", "Suppr.", "Delete", "Löschen", "Eliminar", "Elimina", "Excluir", "削除", "Удалить");
            R("Filter.Settings", "Réglages", "Settings", "Einstellungen", "Ajustes", "Impostazioni", "Configurações", "設定", "Настройки");
            R("Filter.Brightness", "Luminosité", "Brightness", "Helligkeit", "Brillo", "Luminosità", "Brilho", "明るさ", "Яркость");
            R("Filter.Contrast", "Contraste", "Contrast", "Kontrast", "Contraste", "Contrasto", "Contraste", "コントラスト", "Контраст");
            R("Filter.Warmth", "Chaleur", "Warmth", "Wärme", "Calidez", "Calore", "Calor", "色温度", "Теплота");
            R("Filter.Saturation", "Saturation", "Saturation", "Sättigung", "Saturación", "Saturazione", "Saturação", "彩度", "Насыщенность");
            R("Filter.Effects", "Effets", "Effects", "Effekte", "Efectos", "Effetti", "Efeitos", "エフェクト", "Эффекты");
            R("Filter.Vignette", "Vignette", "Vignette", "Vignette", "Viñeta", "Vignettatura", "Vinheta", "ビネット", "Виньетирование");
            R("Filter.VignetteSoftness", "Douceur de la vignette", "Vignette softness", "Vignette-Weichheit", "Suavidad de la viñeta", "Morbidezza vignettatura", "Suavidade da vinheta", "ビネットの柔らかさ", "Мягкость виньетки");
            R("Filter.Grain", "Grain", "Grain", "Filmkorn", "Grano", "Grana", "Granulado", "グレイン", "Зернистость");
            R("Filter.Sepia", "Sépia", "Sepia", "Sepia", "Sepia", "Seppia", "Sépia", "セピア", "Сепия");
            R("Filter.Grayscale", "Niveaux de gris", "Grayscale", "Graustufen", "Escala de grises", "Scala di grigi", "Escala de cinza", "グレースケール", "Оттенки серого");
            R("Filter.Pulse", "Pulsation", "Pulse", "Pulsieren", "Pulso", "Pulsazione", "Pulsação", "パルス", "Пульсация");
            R("Filter.CustomTint", "Teinte personnalisée", "Custom Tint", "Eigene Tönung", "Tinte personalizado", "Tinta personalizzata", "Matiz personalizado", "カスタムティント", "Пользовательский оттенок");
            R("Filter.TintIntensity", "Intensité de la teinte", "Tint intensity", "Tönungsintensität", "Intensidad del tinte", "Intensità tinta", "Intensidade do matiz", "ティントの強さ", "Интенсивность оттенка");
            R("Filter.TintR", "Rouge", "Red", "Rot", "Rojo", "Rosso", "Vermelho", "赤", "Красный");
            R("Filter.TintG", "Vert", "Green", "Grün", "Verde", "Verde", "Verde", "緑", "Зелёный");
            R("Filter.TintB", "Bleu", "Blue", "Blau", "Azul", "Blu", "Azul", "青", "Синий");

            R("Miror.Section", "Miroir", "Mirror", "Spiegel", "Espejo", "Specchio", "Espelho", "鏡", "Зеркало");
            R("Miror.NotLoaded", "Miroir non chargé (va dans la salle City).", "Mirror not loaded (go to the City room).", "Spiegel nicht geladen (gehe in den City-Raum).", "Espejo no cargado (ve a la sala City).", "Specchio non caricato (vai nella stanza City).", "Espelho não carregado (vá para a sala City).", "ミラーが読み込まれていません (Cityルームに行ってください)。", "Зеркало не загружено (зайдите в комнату City).");
            R("Miror.DisabledHint", "Mod désactivé : qualité forcée à x1. Réactive-le depuis la page Mods.", "Mod disabled: quality forced to x1. Re-enable it from the Mods page.", "Mod deaktiviert: Qualität auf x1 erzwungen. Aktiviere ihn erneut auf der Mods-Seite.", "Mod desactivado: calidad forzada a x1. Reactívalo desde la página de Mods.", "Mod disattivata: qualità forzata a x1. Riattivala dalla pagina Mod.", "Mod desativado: qualidade forçada para x1. Reative-o na página de Mods.", "Mod無効: 品質はx1に固定されています。Modページから再度有効にしてください。", "Мод отключён: качество принудительно установлено на x1. Включи его снова на странице Модов.");
            R("Miror.Quality", "Qualité", "Quality", "Qualität", "Calidad", "Qualità", "Qualidade", "品質", "Качество");
            R("Miror.PerfWarning", "Une qualité élevée (>x3) peut réduire les performances.", "High quality (>x3) may reduce performance.", "Hohe Qualität (>x3) kann die Leistung verringern.", "Una calidad alta (>x3) puede reducir el rendimiento.", "Una qualità elevata (>x3) può ridurre le prestazioni.", "Qualidade alta (>x3) pode reduzir o desempenho.", "高品質（x3超）はパフォーマンスを低下させる可能性があります。", "Высокое качество (>x3) может снизить производительность.");

            R("Face.Section", "Visage personnalisé", "Custom Face", "Benutzerdefiniertes Gesicht", "Cara personalizada", "Viso personalizzato", "Rosto personalizado", "カスタムフェイス", "Пользовательское лицо");
            R("Face.Enable", "Visage personnalisé", "Custom face", "Benutzerdefiniertes Gesicht", "Cara personalizada", "Viso personalizzato", "Rosto personalizado", "カスタムフェイス", "Пользовательское лицо");
            R("Face.Folder", "Dossier", "Folder", "Ordner", "Carpeta", "Cartella", "Pasta", "フォルダ", "Папка");
            R("Face.DropHint", "Dépose des .jpg/.png ici, puis Actualiser.", "Drop .jpg/.png here, then Refresh.", "Lege .jpg/.png hier ab, dann Aktualisieren.", "Coloca .jpg/.png aquí, luego Actualizar.", "Metti .jpg/.png qui, poi Aggiorna.", "Coloque .jpg/.png aqui, depois Atualizar.", ".jpg/.pngをここにドロップしてから更新してください。", "Поместите .jpg/.png сюда, затем Обновите.");
            R("Face.Search", "Recherche :", "Search:", "Suche:", "Buscar:", "Cerca:", "Buscar:", "検索：", "Поиск:");
            R("Face.Refresh", "Actualiser", "Refresh", "Aktualisieren", "Actualizar", "Aggiorna", "Atualizar", "更新", "Обновить");
            R("Face.Available", "Visages disponibles", "Available faces", "Verfügbare Gesichter", "Caras disponibles", "Volti disponibili", "Rostos disponíveis", "利用可能なフェイス", "Доступные лица");
            R("Face.NoImages", "Aucune image trouvée.", "No images found.", "Keine Bilder gefunden.", "No se encontraron imágenes.", "Nessuna immagine trovata.", "Nenhuma imagem encontrada.", "画像が見つかりません。", "Изображения не найдены.");
            R("Face.Active", "[actif]", "[active]", "[aktiv]", "[activo]", "[attivo]", "[ativo]", "[アクティブ]", "[активно]");
            R("Face.Select", "Sélectionné.", "Selected.", "Ausgewählt.", "Seleccionado.", "Selezionato.", "Selecionado.", "選択済み。", "Выбрано.");
            R("Face.Selected", "Sélectionné.", "Selected.", "Ausgewählt.", "Seleccionado.", "Selezionato.", "Selecionado.", "選択済み。", "Выбрано.");
            R("Face.Disabled", "Visage désactivé.", "Face disabled.", "Gesicht deaktiviert.", "Cara desactivada.", "Viso disattivato.", "Rosto desativado.", "顔が無効化されました。", "Лицо отключено.");
            R("Face.Error", "Erreur", "Error", "Fehler", "Error", "Errore", "Erro", "エラー", "Ошибка");
            R("Face.FileNotFound", "Fichier introuvable", "File not found", "Datei nicht gefunden", "Archivo no encontrado", "File non trovato", "Arquivo não encontrado", "ファイルが見つかりません", "Файл не найден");
            R("Face.DecodeError", "Erreur de décodage de l'image", "Image decode error", "Fehler beim Dekodieren des Bildes", "Error al decodificar la imagen", "Errore di decodifica dell'immagine", "Erro ao decodificar a imagem", "画像のデコードエラー", "Ошибка декодирования изображения");
            R("Face.DefaultTag", "par défaut", "default", "Standard", "predeterminado", "predefinito", "padrão", "デフォルト", "по умолчанию");
            R("Face.EnableFromModsHint", "Active ce mod depuis la page Mods pour voir ses réglages.", "Enable this mod from the Mods page to see its settings.", "Aktiviere diesen Mod auf der Mods-Seite, um die Einstellungen zu sehen.", "Activa este mod desde la página de Mods para ver sus ajustes.", "Attiva questa mod dalla pagina Mod per vedere le impostazioni.", "Ative este mod na página de Mods para ver as configurações.", "設定を見るには、Modページからこのmodを有効にしてください。", "Включи этот мод на странице Модов, чтобы увидеть настройки.");
            R("Face.SimpleHint", "PNG, dans GorillaFaces à côté du DLL.", "PNG, in GorillaFaces next to the DLL.", "PNG, in GorillaFaces neben der DLL.", "PNG, en GorillaFaces junto al DLL.", "PNG, in GorillaFaces accanto alla DLL.", "PNG, em GorillaFaces ao lado da DLL.", "PNG、DLLの隣のGorillaFacesフォルダ内。", "PNG, в GorillaFaces рядом с DLL.");
            R("Face.RigNotFound", "Rig pas encore détecté.", "Rig not detected yet.", "Rig noch nicht erkannt.", "Rig no detectado todavía.", "Rig non ancora rilevato.", "Rig ainda não detectado.", "Rigがまだ検出されていません。", "Rig ещё не обнаружен.");
            R("Face.Applied", "Visage appliqué :", "Face applied:", "Gesicht angewendet:", "Cara aplicada:", "Viso applicato:", "Rosto aplicado:", "フェイス適用済み：", "Лицо применено:");

            R("Names.Section", "Presets de Noms", "Name Presets", "Namensvoreinstellungen", "Ajustes preestablecidos de nombres", "Preset nomi", "Predefinições de nomes", "名前プリセット", "Пресеты имён");
            R("Names.Outfit", "Tenue :", "Outfit:", "Outfit:", "Atuendo:", "Outfit:", "Traje:", "衣装：", "Наряд:");
            R("Names.Pseudo", "Pseudo :", "Name:", "Name:", "Nombre:", "Nome:", "Nome:", "名前：", "Имя:");
            R("Names.Enable", "Activer", "Enable", "Aktivieren", "Activar", "Attiva", "Ativar", "有効化", "Включить");
            R("Names.Active", "Actif", "Active", "Aktiv", "Activo", "Attivo", "Ativo", "アクティブ", "Активно");
            R("Names.AutoSaveOn", "Auto-save ON", "Auto-save ON", "Auto-Speichern EIN", "Auto-guardar ACTIVADO", "Auto-salva ATTIVA", "Auto-salvar ATIVADO", "自動保存 ON", "Автосохранение ВКЛ");
            R("Names.ClickEnable", "Cliquez Activer", "Click Enable", "Klicke Aktivieren", "Haz clic en Activar", "Clicca Attiva", "Clique em Ativar", "「有効化」をクリック", "Нажмите «Включить»");
            R("Names.Presets", "Presets enregistrés", "Saved presets", "Gespeicherte Voreinstellungen", "Ajustes preestablecidos guardados", "Preset salvati", "Predefinições salvas", "保存済みプリセット", "Сохранённые пресеты");
            R("Names.NoPresets", "Aucun preset.", "No presets.", "Keine Voreinstellungen.", "No hay ajustes preestablecidos.", "Nessun preset.", "Sem predefinições.", "プリセットがありません。", "Нет пресетов.");
            R("Names.EnableFromModsHint", "Active ce mod depuis la page Mods pour voir ses réglages.", "Enable this mod from the Mods page to see its settings.", "Aktiviere diesen Mod auf der Mods-Seite, um die Einstellungen zu sehen.", "Activa este mod desde la página de Mods para ver sus ajustes.", "Attiva questa mod dalla pagina Mod per vedere le impostazioni.", "Ative este mod na página de Mods para ver as configurações.", "設定を見るには、Modページからこのmodを有効にしてください。", "Включи этот мод на странице Модов, чтобы увидеть настройки.");

            R("NameTags.Title", "Étiquettes de Nom", "Name Tags", "Namensschilder", "Etiquetas de nombre", "Etichette nome", "Etiquetas de nome", "ネームタグ", "Именные бирки");
            R("NameTags.Section", "Étiquettes de Nom", "Name Tags", "Namensschilder", "Etiquetas de nombre", "Etichette nome", "Etiquetas de nome", "ネームタグ", "Именные бирки");
            R("NameTags.EnableFromModsHint", "Active ce mod depuis la page Mods pour voir ses réglages.", "Enable this mod from the Mods page to see its settings.", "Aktiviere diesen Mod auf der Mods-Seite, um die Einstellungen zu sehen.", "Activa este mod desde la página de Mods para ver sus ajustes.", "Attiva questa mod dalla pagina Mod per vedere le impostazioni.", "Ative este mod na página de Mods para ver as configurações.", "設定を見るには、Modページからこのmodを有効にしてください。", "Включи этот мод на странице Модов, чтобы увидеть настройки.");

            R("NameTags.BaseSize", "Taille de base", "Base size", "Grundgröße", "Tamaño base", "Dimensione base", "Tamanho base", "基本サイズ", "Базовый размер");
            R("NameTags.BaseSizeShort", "Taille", "Size", "Größe", "Tamaño", "Dimensione", "Tamanho", "サイズ", "Размер");

            R("NameTags.HeightOffset", "Hauteur au-dessus de la tête", "Height above head", "Höhe über dem Kopf", "Altura sobre la cabeza", "Altezza sopra la testa", "Altura acima da cabeça", "頭上の高さ", "Высота над головой");
            R("NameTags.HeightOffsetShort", "Hauteur", "Height", "Höhe", "Altura", "Altezza", "Altura", "高さ", "Высота");

            R("NameTags.MaxRenderDistance", "Distance d'affichage maximale", "Max render distance", "Maximale Anzeigedistanz", "Distancia máxima de renderizado", "Distanza massima di rendering", "Distância máxima de renderização", "最大表示距離", "Максимальная дистанция отображения");
            R("NameTags.MaxRenderDistanceShort", "Max", "Max", "Max", "Máx", "Max", "Máx", "最大", "Макс");

            R("NameTags.IconMode", "Icône de plateforme", "Platform icon", "Plattform-Symbol", "Icono de plataforma", "Icona piattaforma", "Ícone de plataforma", "プラットフォームアイコン", "Значок платформы");
            R("NameTags.IconOff", "Aucune", "Off", "Aus", "Ninguno", "Nessuna", "Nenhum", "なし", "Нет");
            R("NameTags.IconLeft", "Gauche", "Left", "Links", "Izquierda", "Sinistra", "Esquerda", "左", "Слева");
            R("NameTags.IconRight", "Droite", "Right", "Rechts", "Derecha", "Destra", "Direita", "右", "Справа");

            R("NameTags.IconGap", "Espacement entre le texte et l'icône", "Gap between text and icon", "Abstand zwischen Text und Symbol", "Espacio entre el texto y el icono", "Spazio tra testo e icona", "Espaço entre o texto e o ícone", "テキストとアイコンの間隔", "Расстояние между текстом и значком");
            R("NameTags.IconGapShort", "Espace", "Gap", "Abstand", "Espacio", "Spazio", "Espaço", "間隔", "Отступ");

            R("NameTags.Hint",
                "L'étiquette regarde toujours vers toi et rétrécit avec la distance.",
                "The tag always faces you and shrinks with distance.",
                "Das Schild schaut immer zu dir und wird mit der Entfernung kleiner.",
                "La etiqueta siempre te mira y se reduce con la distancia.",
                "L'etichetta è sempre rivolta verso di te e si rimpicciolisce con la distanza.",
                "A etiqueta sempre olha para você e diminui com a distância.",
                "タグは常にあなたの方を向き、距離とともに縮小します。",
                "Бирка всегда смотрит на тебя и уменьшается с расстоянием.");
        }
    }
}
