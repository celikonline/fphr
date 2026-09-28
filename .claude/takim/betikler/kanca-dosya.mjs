// takim-kur — PreToolUse kancası: dosya araçları (Read, Grep, Write, Edit, MultiEdit, NotebookEdit).
// Kopyalanır: .claude/takim/betikler/kanca-dosya.mjs  (elle değiştirmeyin; /takim-kur günceller)
//
// Engellenenler (exit 2 + Türkçe gerekçe):
//   - sır dosyasını OKUMA ya da YAZMA (.env, .env.*, *.pem, *.key, id_rsa…; .env.example serbest)
//   - takımın kendi dosyalarını değiştirme (.claude/takim/**, .claude/settings*.json)
//   - proje kökü DIŞINA yazma — izinli olanlar: sistem geçici dizini, ~/.claude (Claude'un kendi
//     planları/hafızası) ve takim.json "yazilabilir_disari" listesi. Kök dışını okumak serbesttir.
// Kancalar kaza önleyicidir, güvenlik sınırı değildir (bkz. ortak.mjs).

import { resolve, basename, isAbsolute, relative, join } from 'node:path';
import { tmpdir, homedir } from 'node:os';
import {
  projeKoku, ayarOku, girdiOku, sirDosyasiMi, korunanTakimYoluMu, engelle, guvenliCalistir, gercekYol,
} from './ortak.mjs';

const YAZANLAR = new Set(['Write', 'Edit', 'MultiEdit', 'NotebookEdit']);
const OKUYANLAR = new Set(['Read', 'Grep', 'NotebookRead']);
const buyukKucuk = process.platform === 'win32' || process.platform === 'darwin';

function altindaMi(yol, dizin) {
  const a = buyukKucuk ? yol.toLowerCase() : yol;
  const b = buyukKucuk ? dizin.toLowerCase() : dizin;
  const rel = relative(b, a);
  return rel === '' || (!rel.startsWith('..') && !isAbsolute(rel));
}

export function denetleDosya(arac, dosya, { cwd, kok = projeKoku() } = {}) {
  if (typeof dosya !== 'string' || !dosya) return;
  const yaziyor = YAZANLAR.has(arac);
  if (!yaziyor && !OKUYANLAR.has(arac)) return;

  if (sirDosyasiMi(dosya)) {
    if (yaziyor) engelle('SIR-YAZ', `sır dosyasına yazılmaz (${dosya}). Sırlar insan tarafından, depo dışında yönetilir; örnek değerler için .env.example.`);
    engelle('SIR-OKU', `sır dosyası okunmaz (${dosya}). Değişken adları için .env.example kullanın; değerler sohbete ve kayıtlara girmemeli.`);
  }
  if (!yaziyor) return;

  const kokGercek = gercekYol(kok);
  const tam = gercekYol(isAbsolute(dosya) || /^[A-Za-z]:[\\/]/.test(dosya) ? dosya : resolve(cwd || kok, dosya));
  if (altindaMi(tam, kokGercek)) {
    const goreli = relative(kokGercek, tam).split(/[\\/]/).join('/');
    if (korunanTakimYoluMu(goreli)) {
      engelle('TAKIM-KORUMA', `takımın kendi dosyaları değiştirilemez (${goreli}). Güncelleme/kaldırma için /takim-kur kullanın ya da bir insan elle yapsın.`);
    }
    return;
  }
  const izinli = [tmpdir(), join(homedir(), '.claude'), ...ayarOku(kok).yazilabilir_disari.map((d) => resolve(kok, d))]
    .map(gercekYol);
  if (!izinli.some((d) => altindaMi(tam, d))) {
    engelle('KOK-DISI', `proje kökü dışına yazılmaz (${dosya}). Başka bir repoya dokunmak ayrı bir iştir; gerekiyorsa dizini takim.json "yazilabilir_disari" listesine bir insan eklesin.`);
  }
}

if (process.argv[1] && basename(process.argv[1]) === 'kanca-dosya.mjs') {
  guvenliCalistir(() => {
    const girdi = girdiOku();
    const t = girdi.tool_input || {};
    denetleDosya(girdi.tool_name, t.file_path || t.notebook_path || t.path, { cwd: girdi.cwd });
  });
}
