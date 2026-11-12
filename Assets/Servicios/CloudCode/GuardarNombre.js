/**
 * GuardarNombre — Cloud Code (JavaScript) de ApexRun.
 *
 * Guarda el nombre de la contrarreloj del jugador (dato público "tt_nombre",
 * el que muestra la tabla). El juego no puede escribir en Cloud Save (Access
 * Control), así que pasa por acá: se limpia igual que en el juego
 * (TimeTrialRecords.CleanName) y hay un límite de cambios por jugador.
 */

const axios = require("axios-1.6");

const LIMITS = { sendsWindow: 600, maxSends: 10, minSendGap: 3 };

module.exports = async ({ params, context, logger }) => {
  try {
    const { projectId, playerId } = context;
    const name = cleanName(params.nombre);

    if (!name) return { ok: false, codigo: "nombre" };

    const api = services(context);
    const limit = await countSend(api, projectId, playerId, "tt_nombre_envios");
    if (limit) return { ok: false, codigo: limit };

    await api.setPublic(projectId, playerId, "tt_nombre", name);
    return { ok: true, codigo: "ok", nombre: name };
  } catch (error) {
    logger.error("GuardarNombre: error interno", { "error.message": error && error.message });
    return { ok: false, codigo: "servidor" };
  }
};

module.exports.params = {
  nombre: { type: "String", required: true }
};

/** Sin < > ni caracteres de control, espacios simples, hasta 16 caracteres. */
function cleanName(value) {
  if (typeof value !== "string") return "";
  let name = value.replace(/[\u0000-\u001f\u007f<>]/g, " ").replace(/\s+/g, " ").trim();
  if (name.length > 16) name = name.slice(0, 16).trim();
  return name;
}

async function countSend(api, projectId, playerId, key) {
  const now = Date.now() / 1000;
  const item = await api.getProtected(projectId, playerId, key);
  const times = item && item.value && Array.isArray(item.value.t) ? item.value.t.filter(t => typeof t === "number" && now - t < LIMITS.sendsWindow) : [];

  if (times.length > 0 && now - times[times.length - 1] < LIMITS.minSendGap) return "muchos";
  if (times.length >= LIMITS.maxSends) return "muchos";

  times.push(now);
  try {
    const body = { key, value: { t: times } };
    if (item && item.writeLock) body.writeLock = item.writeLock;
    await api.setProtected(projectId, playerId, body);
  } catch (error) {
    if (error.response && error.response.status === 409) return "ocupado";
    throw error;
  }
  return null;
}

function services(context) {
  const config = {
    headers: { Authorization: "Bearer " + context.serviceToken, "Content-Type": "application/json" },
    timeout: 8000
  };
  const save = "https://cloud-save.services.api.unity.com/v1/data/projects/";

  return {
    async getProtected(projectId, playerId, key) {
      const res = await axios.get(save + projectId + "/players/" + playerId + "/protected/items?keys=" + encodeURIComponent(key), config);
      return (res.data && res.data.results && res.data.results.find(r => r.key === key)) || null;
    },
    async setProtected(projectId, playerId, body) {
      await axios.post(save + projectId + "/players/" + playerId + "/protected/items", body, config);
    },
    async setPublic(projectId, playerId, key, value) {
      await axios.post(save + projectId + "/players/" + playerId + "/public/items", { key, value }, config);
    }
  };
}

module.exports.internals = { cleanName };
