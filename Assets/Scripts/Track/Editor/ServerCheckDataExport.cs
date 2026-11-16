using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Actualiza los datos que usa el script de Cloud Code SubirMarca.js para
/// revisar las vueltas de la contrarreloj en el servidor: los checkpoints de la
/// escena Race (centro, ejes y medidas de cada BoxCollider) y, de las piezas,
/// la velocidad de cada motor al corte de rpm, el radio y el agarre de cada
/// juego de ruedas y la carga aerodinámica de cada kit.
/// Usarlo cada vez que cambie la pista o se agregue o cambie una pieza, y después
/// desplegar el script (Services > Deployment). Si no, el servidor podría
/// rechazar vueltas buenas (o dejar pasar de más).
/// Reemplaza solo lo que está entre "// &lt;datos&gt;" y "// &lt;/datos&gt;".
///
/// También copia los parámetros de cada script (module.exports.params) a su
/// importador (el Inspector del .js): el proyecto no tiene el proyecto de Node
/// de Cloud Code, así que Unity no los lee solo, y Cloud Code descarta los
/// parámetros que el script no declaró al desplegarse.
/// </summary>
public static class ServerCheckDataExport
{
    private const string ScriptPath = "Assets/Servicios/CloudCode/SubirMarca.js";
    private const string ScriptsFolder = "Assets/Servicios/CloudCode";
    private const string RaceScenePath = "Assets/Scenes/Race.unity";
    private const string StartMark = "// <datos>";
    private const string EndMark = "// </datos>";

    [MenuItem("ApexRun/Servicios/Actualizar datos de SubirMarca")]
    public static void Export()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("SubirMarca", "Salí de Play primero.", "OK");
            return;
        }

        if (!File.Exists(ScriptPath))
        {
            EditorUtility.DisplayDialog("SubirMarca", "No está " + ScriptPath + ".", "OK");
            return;
        }

        string block;

        try
        {
            block = BuildBlock();
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("SubirMarca", "No se pudieron leer los datos: " + exception.Message, "OK");
            return;
        }

        string text = File.ReadAllText(ScriptPath);
        int start = text.IndexOf(StartMark, StringComparison.Ordinal);
        int end = text.IndexOf(EndMark, StringComparison.Ordinal);

        if (start < 0 || end < start)
        {
            EditorUtility.DisplayDialog("SubirMarca", "El script no tiene las marcas " + StartMark + " y " + EndMark + ".", "OK");
            return;
        }

        string newLine = text.Contains("\r\n") ? "\r\n" : "\n";
        string updated = text.Substring(0, start) + block.Replace("\n", newLine) + text.Substring(end);

        if (updated == text)
        {
            SyncParameters();
            Debug.Log("SubirMarca: los datos ya estaban al día.");
            return;
        }

        File.WriteAllText(ScriptPath, updated);
        AssetDatabase.ImportAsset(ScriptPath);
        SyncParameters();

        Debug.Log("SubirMarca: datos actualizados. Falta desplegar el script (Services > Deployment).");
        EditorUtility.DisplayDialog("SubirMarca", "Datos actualizados. Ahora desplegá SubirMarca.js desde Services > Deployment.", "OK");
    }

    [MenuItem("ApexRun/Servicios/Actualizar parámetros de Cloud Code")]
    public static void SyncParametersMenu()
    {
        int changed = SyncParameters();
        Debug.Log(changed == 0
            ? "Cloud Code: los parámetros ya estaban al día."
            : "Cloud Code: parámetros actualizados en " + changed + " script(s). Falta desplegarlos (Services > Deployment).");
    }

    /// <summary>
    /// Copia module.exports.params de cada .js de la carpeta al importador del
    /// script (los que despliega Services > Deployment). Devuelve cuántos cambiaron.
    /// </summary>
    public static int SyncParameters()
    {
        int changed = 0;

        if (!Directory.Exists(ScriptsFolder))
            return 0;

        foreach (string file in Directory.GetFiles(ScriptsFolder, "*.js"))
        {
            string path = file.Replace('\\', '/');
            AssetImporter importer = AssetImporter.GetAtPath(path);

            if (importer == null)
                continue;

            var serialized = new SerializedObject(importer);
            SerializedProperty list = serialized.FindProperty("Parameters");

            if (list == null || !list.isArray)
                continue;

            List<ScriptParameter> parameters = ReadParameters(File.ReadAllText(path));

            if (parameters == null)
            {
                Debug.LogWarning("Cloud Code: no se pudieron leer los parámetros de " + path + ".");
                continue;
            }

            if (SameParameters(list, parameters))
                continue;

            list.arraySize = parameters.Count;

            for (int i = 0; i < parameters.Count; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Name").stringValue = parameters[i].Name;
                element.FindPropertyRelative("ParameterType").enumValueIndex = parameters[i].Type;
                element.FindPropertyRelative("Required").boolValue = parameters[i].Required;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
            changed++;
        }

        return changed;
    }

    private struct ScriptParameter
    {
        public string Name;
        public int Type;
        public bool Required;
    }

    // En el orden del enum ParameterType de Cloud Code.
    private static readonly string[] ParameterTypes = { "String", "Numeric", "Boolean", "JSON", "Any" };

    /// <summary>
    /// Los parámetros de "module.exports.params = { nombre: { type: "String", required: true }, ... }"
    /// (o "nombre: "String""). Sin el bloque, ninguno; si algo no se entiende, null.
    /// </summary>
    private static List<ScriptParameter> ReadParameters(string text)
    {
        var parameters = new List<ScriptParameter>();
        int at = text.IndexOf("module.exports.params", StringComparison.Ordinal);

        if (at < 0)
            return parameters;

        int open = text.IndexOf('{', at);
        int close = -1;
        int depth = 0;

        for (int i = open; open >= 0 && i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
            {
                close = i;
                break;
            }
        }

        if (close < 0)
            return null;

        string body = Regex.Replace(text.Substring(open + 1, close - open - 1), @"//[^\n]*", "");

        foreach (Match match in Regex.Matches(body, @"(\w+)\s*:\s*(\{[^}]*\}|""\w+"")"))
        {
            string definition = match.Groups[2].Value;
            int type = Array.IndexOf(ParameterTypes, Regex.Match(definition, @"""(\w+)""").Groups[1].Value);

            if (type < 0)
                return null;

            parameters.Add(new ScriptParameter
            {
                Name = match.Groups[1].Value,
                Type = type,
                Required = Regex.IsMatch(definition, @"required\s*:\s*true")
            });
        }

        return parameters;
    }

    private static bool SameParameters(SerializedProperty list, List<ScriptParameter> parameters)
    {
        if (list.arraySize != parameters.Count)
            return false;

        for (int i = 0; i < parameters.Count; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);

            if (element.FindPropertyRelative("Name").stringValue != parameters[i].Name
                || element.FindPropertyRelative("ParameterType").enumValueIndex != parameters[i].Type
                || element.FindPropertyRelative("Required").boolValue != parameters[i].Required)
                return false;
        }

        return true;
    }

    /// <summary>El bloque entre las marcas (sin la marca del final), con "\n".</summary>
    public static string BuildBlock()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.Append(StartMark).Append(" (generado por ServerCheckDataExport.cs el ")
          .Append(DateTime.Now.ToString("dd/MM/yyyy", inv)).Append("; no editar a mano)\n");

        // ---------- Checkpoints ----------
        sb.Append("const GATES = [\n");

        List<RaceCheckpoint> checkpoints = ReadCheckpoints(out List<string> gates);

        if (checkpoints.Count == 0)
            throw new InvalidOperationException("la escena Race no tiene checkpoints");

        for (int i = 0; i < gates.Count; i++)
            sb.Append("  ").Append(gates[i]).Append(i < gates.Count - 1 ? ",\n" : "\n");

        sb.Append("];\n\n");

        // ---------- Motores ----------
        sb.Append("// Velocidad del eje al corte de rpm (rad/s) de cada motor: maxRpm * 2π / 60 / gearRatio.\n");
        var engines = new List<string>();

        foreach (EngineData engine in LoadAll<EngineData>())
        {
            if (engine == null || string.IsNullOrWhiteSpace(engine.partID) || engine.engineSettings == null)
                continue;

            double axle = engine.engineSettings.maxRpm * 2.0 * Math.PI / 60.0 / Math.Max(0.1, engine.engineSettings.gearRatio);
            engines.Add(string.Format(inv, "\"{0}\": {1:0.###}", engine.partID, axle));
        }

        sb.Append("const ENGINES = {\n  ").Append(string.Join(", ", engines)).Append("\n};\n\n");

        // ---------- Ruedas ----------
        var radii = new List<string>();
        var grips = new List<string>();

        foreach (WheelData wheels in LoadAll<WheelData>())
        {
            if (wheels == null || string.IsNullOrWhiteSpace(wheels.partID) || wheels.tireSettings == null)
                continue;

            radii.Add(string.Format(inv, "\"{0}\": {1:0.###}", wheels.partID, wheels.tireSettings.radius));
            grips.Add(string.Format(inv, "\"{0}\": {1:0.###}", wheels.partID, wheels.tireSettings.lateralFriction));
        }

        sb.Append("// Radio de las ruedas (m).\n");
        sb.Append("const WHEELS = {\n  ").Append(string.Join(", ", radii)).Append("\n};\n\n");
        sb.Append("// Agarre de costado de cada goma (TireSettings.lateralFriction).\n");
        sb.Append("const WHEEL_GRIP = {\n  ").Append(string.Join(", ", grips)).Append("\n};\n\n");

        // ---------- Kits aerodinámicos ----------
        var areas = new List<string>();

        foreach (AeroKitData aero in LoadAll<AeroKitData>())
        {
            if (aero == null || string.IsNullOrWhiteSpace(aero.partID) || aero.aeroSettings == null)
                continue;

            areas.Add(string.Format(inv, "\"{0}\": {1:0.###}", aero.partID, aero.aeroSettings.downforceArea));
        }

        sb.Append("// Superficie de carga aerodinámica de cada kit (AeroSettings.downforceArea, m²).\n");
        sb.Append("const AERO_AREA = {\n  ").Append(string.Join(", ", areas)).Append("\n};\n");

        return sb.ToString();
    }

    /// <summary>Los checkpoints de Race (abre la escena aparte si no está abierta, sin guardarla).</summary>
    private static List<RaceCheckpoint> ReadCheckpoints(out List<string> gates)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        gates = new List<string>();

        Scene scene = SceneManager.GetSceneByPath(RaceScenePath);
        bool opened = false;

        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(RaceScenePath, OpenSceneMode.Additive);
            opened = true;
        }

        try
        {
            // Los de la carrera: la lista de RaceCheckpointManager (puede haber otros en la escena).
            RaceCheckpointManager manager = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (manager == null)
                    manager = root.GetComponentInChildren<RaceCheckpointManager>(true);
            }

            if (manager == null)
                throw new InvalidOperationException("la escena Race no tiene RaceCheckpointManager");

            var checkpoints = new List<RaceCheckpoint>();
            SerializedProperty list = new SerializedObject(manager).FindProperty("checkpoints");

            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is RaceCheckpoint checkpoint)
                    checkpoints.Add(checkpoint);
            }

            checkpoints.Sort((a, b) => a.CheckpointIndex.CompareTo(b.CheckpointIndex));

            for (int i = 0; i < checkpoints.Count; i++)
            {
                if (checkpoints[i].CheckpointIndex != i)
                    throw new InvalidOperationException("falta el checkpoint " + i);

                Transform t = checkpoints[i].transform;
                BoxCollider box = checkpoints[i].GetComponent<BoxCollider>();

                if (box == null)
                    throw new InvalidOperationException("el checkpoint " + i + " no tiene BoxCollider");

                Vector3 c = t.TransformPoint(box.center);
                Vector3 s = Vector3.Scale(t.lossyScale, box.size) * 0.5f;

                gates.Add(string.Format(inv,
                    "{{\"i\":{0},\"c\":[{1:0.###},{2:0.###},{3:0.###}],\"r\":[{4:0.####},{5:0.####},{6:0.####}],\"u\":[{7:0.####},{8:0.####},{9:0.####}],\"f\":[{10:0.####},{11:0.####},{12:0.####}],\"h\":[{13:0.###},{14:0.###},{15:0.###}]}}",
                    i, c.x, c.y, c.z,
                    t.right.x, t.right.y, t.right.z,
                    t.up.x, t.up.y, t.up.z,
                    t.forward.x, t.forward.y, t.forward.z,
                    Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
            }

            return checkpoints;
        }
        finally
        {
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static IEnumerable<T> LoadAll<T>() where T : UnityEngine.Object
    {
        var list = new List<T>();

        foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

            if (asset != null)
                list.Add(asset);
        }

        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }
}
