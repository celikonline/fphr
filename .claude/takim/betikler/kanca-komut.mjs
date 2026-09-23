// takim-kur — PreToolUse kancası: Bash ve PowerShell komutları.
// Kopyalanır: .claude/takim/betikler/kanca-komut.mjs  (elle değiştirmeyin; /takim-kur günceller)
//
// Düz regex yerine komut AYRIŞTIRILIR: tırnaklar, bileşik komutlar (&& || ; |), yönlendirmeler,
// `bash -c "…"` / `powershell -Command "…"` içindeki komutlar ve git'in global seçenekleri (-C, -c,
// --git-dir) dikkate alınır. Böylece `git -C x push --force` yakalanır, commit mesajındaki "--force"
// metni ise yanlış alarm vermez.
//
// Engellenenler (exit 2 + Türkçe gerekçe):
//   - push: --force / -f / --force-with-lease / +refspec / --mirror / --all / --no-verify,
//           korumalı dala push (açık refspec ya da korumalı daldayken refspec'siz push), korumalı dalı silme
//   - korumalı daldayken commit / merge / rebase / cherry-pick / revert / am
//   - commit --no-verify (-n), -c core.hooksPath=…, git config core.hooksPath
//   - reset --hard, clean -f (dry-run hariç), filter-branch / filter-repo, update-ref korumalı dal,
//     branch -D/-M/-f korumalı dal, checkout/switch -B/-C korumalı dal
//   - sır dosyasına yazma (>, >>, tee, cp/mv hedefi, sed -i, Set-Content/Out-File…) ve sır dosyasını
//     okuma (cat/type/Get-Content/head/tail/less, < yönlendirme)
//   - takımın kendi dosyalarını (.claude/takim/**, .claude/settings*.json) değiştirme/silme
// Bilinen, ENGELLENMEYEN aşma yolları: node -e / python -c ile yazma, betik dosyası yazıp çalıştırma,
// git alias, eval ile üretilen komutlar. Asıl koruma uzak depodaki dal korumasıdır.

import { execFileSync } from 'node:child_process';
import { resolve, basename } from 'node:path';
import {
  projeKoku, ayarOku, girdiOku, sirDosyasiMi, kokeGoreli, korunanTakimYoluMu, engelle, guvenliCalistir,
} from './ortak.mjs';

// ---------------- ayrıştırıcı ----------------

/**
 * Komut metnini kabuk benzeri kurallarla parçalara böler.
 * Dönen: [{ kelimeler: string[], yonler: [{ op, hedef }] }]  (her eleman bir basit komut)
 */
export function parcala(metin) {
  const parcalar = [];
  let kelimeler = [];
  let yonler = [];
  let buf = '';
  let bufVar = false; // boş tırnak ("") da bir kelimedir
  let bekleyenYon = null; // hedefi beklenen yönlendirme

  const kelimeBitir = () => {
    if (!bufVar) return;
    if (bekleyenYon) {
      yonler.push({ op: bekleyenYon, hedef: buf });
      bekleyenYon = null;
    } else {
      kelimeler.push(buf);
    }
    buf = '';
    bufVar = false;
  };
  const parcaBitir = () => {
    kelimeBitir();
    bekleyenYon = null;
    if (kelimeler.length || yonler.length) parcalar.push({ kelimeler, yonler });
    kelimeler = [];
    yonler = [];
  };

  for (let i = 0; i < metin.length; i++) {
    const c = metin[i];
    const s = metin[i + 1];
    // Heredoc (<<EOF … EOF, <<-'EOF'): içerik komut DEĞİL, metindir — atlanır (README yazarken
    // içinde "git push --force" geçmesi yanlış alarm vermesin). Here-string (<<<) ise tek kelimedir.
    if (c === '<' && s === '<' && metin[i + 2] !== '<') {
      kelimeBitir();
      let j = i + 2;
      const eksi = metin[j] === '-';
      if (eksi) j += 1;
      while (metin[j] === ' ' || metin[j] === '\t') j += 1;
      const m = /^(['"]?)([A-Za-z_][A-Za-z0-9_]*)\1/.exec(metin.slice(j));
      if (m) {
        const sinir = m[2];
        const satirSonu = metin.indexOf('\n', j);
        // Heredoc'lu satırın kalanı (ör. "> dosya") yine komuttur: onu ayrıştır, sonra içeriği atla.
        const satirKalani = satirSonu === -1 ? metin.slice(j + m[0].length) : metin.slice(j + m[0].length, satirSonu);
        const alt = parcala(satirKalani);
        if (alt.length) { kelimeler.push(...alt[0].kelimeler); yonler.push(...alt[0].yonler); }
        if (satirSonu === -1) { i = metin.length; continue; }
        let k = satirSonu + 1;
        for (;;) {
          const son = metin.indexOf('\n', k);
          const satir = (son === -1 ? metin.slice(k) : metin.slice(k, son)).replace(/\r$/, '');
          if ((eksi ? satir.replace(/^\t+/, '') : satir) === sinir || son === -1) { k = son === -1 ? metin.length : son; break; }
          k = son + 1;
        }
        parcaBitir();
        i = k;
        continue;
      }
    }
    // PowerShell here-string: @' … '@ / @" … "@ — içerik metindir.
    if (c === '@' && (s === "'" || s === '"') && /^\r?\n/.test(metin.slice(i + 2))) {
      const bitis = metin.indexOf(`\n${s}@`, i + 2);
      buf += bitis === -1 ? metin.slice(i + 2) : metin.slice(i + 2, bitis);
      bufVar = true;
      i = bitis === -1 ? metin.length : bitis + 2;
      continue;
    }
    if (c === "'") { // tek tırnak: olduğu gibi
      const bit = metin.indexOf("'", i + 1);
      buf += bit === -1 ? metin.slice(i + 1) : metin.slice(i + 1, bit);
      bufVar = true;
      i = bit === -1 ? metin.length : bit;
      continue;
    }
    if (c === '"') { // çift tırnak: \" ve `" kaçışları
      let j = i + 1;
      while (j < metin.length && metin[j] !== '"') {
        if ((metin[j] === '\\' || metin[j] === '`') && metin[j + 1] === '"') { buf += '"'; j += 2; continue; }
        buf += metin[j];
        j += 1;
      }
      bufVar = true;
      i = j;
      continue;
    }
    if (c === ' ' || c === '\t' || c === '\r') { kelimeBitir(); continue; }
    if (c === '\n' || c === ';' || c === '(' || c === ')' || c === '{' || c === '}') { parcaBitir(); continue; }
    if (c === '$' && s === '(') { parcaBitir(); i += 1; continue; } // $( … ) içi ayrı komut
    if (c === '`' && !bufVar) { parcaBitir(); continue; } // `…` komut ikamesi (bash)
    if (c === '|') { parcaBitir(); if (s === '|') i += 1; continue; }
    if (c === '&' && s === '&') { parcaBitir(); i += 1; continue; }
    if (c === '>' || (c === '&' && s === '>') || c === '<') {
      // Yönlendirme. "2>" gibi fd öneki: tampon yalnız rakamsa operatöre aittir.
      let op = '';
      if (/^\d+$/.test(buf) || buf === '*') { op = buf; buf = ''; bufVar = false; } else kelimeBitir();
      if (c === '&') { op += '&'; i += 1; }
      op += metin[i];
      if (c === '<') { while (metin[i + 1] === '<') { i += 1; op += '<'; } } // <<< here-string: hedef bir kelime
      while (metin[i + 1] === '>' || metin[i + 1] === '|') { i += 1; op += metin[i]; }
      if (metin[i + 1] === '&') { // >&1, 2>&1: dosya değil fd kopyası
        i += 1;
        let fd = '';
        while (/\d|-/.test(metin[i + 1] || '')) { i += 1; fd += metin[i]; }
        yonler.push({ op: op + '&', hedef: `&${fd}` });
        continue;
      }
      bekleyenYon = op;
      continue;
    }
    if (c === '&') { parcaBitir(); continue; } // arka plan / PowerShell çağrı operatörü
    buf += c;
    bufVar = true;
  }
  parcaBitir();
  return parcalar;
}

// ---------------- yardımcılar ----------------

const SARMALAYICI = new Set(['sudo', 'command', 'exec', 'nohup', 'time', 'nice', 'builtin', 'noglob']);
const komutAdi = (k) => basename(String(k).replace(/\\/g, '/')).toLowerCase().replace(/\.(exe|cmd|bat|ps1)$/, '');
const secenekMi = (k) => /^-/.test(k) && k !== '-';
const kisaKume = (k, harf) => /^-[a-zA-Z]+$/.test(k) && k.slice(1).includes(harf);

/** Ortam atamalarını ve sarmalayıcıları atlar: [ad, ...argümanlar] */
function asilKomut(kelimeler) {
  let i = 0;
  for (;;) {
    const k = kelimeler[i];
    if (k === undefined) return [];
    if (/^[A-Za-z_][A-Za-z0-9_]*=/.test(k)) { i += 1; continue; }
    const ad = komutAdi(k);
    if (SARMALAYICI.has(ad)) { i += 1; continue; }
    if (ad === 'env') { i += 1; while (kelimeler[i] && (secenekMi(kelimeler[i]) || kelimeler[i].includes('='))) i += 1; continue; }
    return [ad, ...kelimeler.slice(i + 1)];
  }
}

const dalAdi = (ref) => String(ref).replace(/^refs\/heads\//, '');

// ---------------- kurallar ----------------

function yolKontrol(hedef, cwd, kok, niyet) {
  const t = String(hedef).trim();
  if (!t || /^&\d*-?$/.test(t) || /^(\/dev\/(null|stdout|stderr)|nul|\$null|con)$/i.test(t)) return;
  if (sirDosyasiMi(t)) {
    if (niyet === 'oku') engelle('SIR-OKU', `sır dosyası okunmaz (${t}). Değişken adları için .env.example kullanın; değerler sohbete ve kayıtlara girmemeli.`);
    engelle('SIR-YAZ', `sır dosyasına komutla yazılmaz (${t}). Sırlar insan tarafından, depo dışında yönetilir.`);
  }
  if (niyet !== 'oku' && korunanTakimYoluMu(kokeGoreli(t, cwd, kok))) {
    engelle('TAKIM-KORUMA', `takımın kendi dosyaları değiştirilemez (${t}). Güncelleme/kaldırma için /takim-kur kullanın ya da bir insan elle yapsın.`);
  }
}

const OKUYANLAR = new Set([
  'cat', 'type', 'get-content', 'gc', 'head', 'tail', 'less', 'more', 'bat', 'nl', 'tac',
  'grep', 'egrep', 'fgrep', 'rg', 'findstr', 'awk', 'strings', 'base64', 'xxd', 'od', 'select-string', 'sls',
]);
const SILENLER = new Set(['rm', 'del', 'erase', 'remove-item', 'ri', 'rmdir', 'rd', 'unlink', 'shred']);
const TASIYANLAR = new Set(['mv', 'move', 'move-item', 'mi', 'ren', 'rename', 'rename-item', 'rni']);
const KOPYALAYANLAR = new Set(['cp', 'copy', 'copy-item', 'cpi', 'install', 'rsync', 'robocopy', 'xcopy']);
const PS_YAZANLAR = new Set(['set-content', 'sc', 'add-content', 'ac', 'out-file', 'new-item', 'ni', 'clear-content', 'clc']);
const PS_YOL_PARAM = /^-(path|filepath|literalpath|destination|target|value)?$/i;

/** PowerShell parametreli çağrıda hedef yol(lar): -Path/-FilePath/-LiteralPath/-Destination değeri ya da ilk konumsal. */
function psYollari(args, paramlar) {
  const yollar = [];
  let konumsal = null;
  for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (/^-/.test(a)) {
      const ad = a.replace(/:.*$/, '').toLowerCase();
      if (paramlar.includes(ad)) {
        if (a.includes(':')) yollar.push(a.slice(a.indexOf(':') + 1));
        else if (args[i + 1] !== undefined) { yollar.push(args[i + 1]); i += 1; }
      } else if (PS_YOL_PARAM.test(a) && args[i + 1] !== undefined && !/^-/.test(args[i + 1])) {
        i += 1; // değer alan başka bir parametre (ör. -Value): atla
      }
      continue;
    }
    if (konumsal === null) konumsal = a;
  }
  if (!yollar.length && konumsal !== null) yollar.push(konumsal);
  return yollar;
}

function dosyaKomutKontrol(ad, args, cwd, kok) {
  const konumsallar = args.filter((a) => !secenekMi(a));
  if (OKUYANLAR.has(ad)) {
    const psOkuyan = ['get-content', 'gc', 'select-string', 'sls'].includes(ad);
    const yollar = psOkuyan ? psYollari(args, ['-path', '-literalpath']).concat(konumsallar) : konumsallar;
    yollar.forEach((y) => yolKontrol(y, cwd, kok, 'oku'));
  } else if (SILENLER.has(ad)) {
    const yollar = ad === 'remove-item' || ad === 'ri' ? psYollari(args, ['-path', '-literalpath']).concat(konumsallar) : konumsallar;
    yollar.forEach((y) => { if (korunanTakimYoluMu(kokeGoreli(y, cwd, kok))) yolKontrol(y, cwd, kok, 'yaz'); });
  } else if (TASIYANLAR.has(ad)) {
    konumsallar.forEach((y) => yolKontrol(y, cwd, kok, 'yaz')); // kaynak da hedef de: korunan dosyayı taşımak da onu bozar
  } else if (KOPYALAYANLAR.has(ad)) {
    const ps = ad === 'copy-item' || ad === 'cpi';
    const hedef = ps ? psYollari(args, ['-destination']).slice(-1) : konumsallar.slice(-1);
    const kaynaklar = ps ? psYollari(args, ['-path', '-literalpath']) : konumsallar.slice(0, -1);
    kaynaklar.forEach((y) => yolKontrol(y, cwd, kok, 'oku')); // sırrı kopyalayarak dışarı çıkarmak da okumadır
    hedef.forEach((y) => yolKontrol(y, cwd, kok, 'yaz'));
  } else if (ad === 'tee' || ad === 'tee-object') {
    (ad === 'tee' ? konumsallar : psYollari(args, ['-filepath', '-literalpath', '-path'])).forEach((y) => yolKontrol(y, cwd, kok, 'yaz'));
  } else if (PS_YAZANLAR.has(ad)) {
    psYollari(args, ['-path', '-filepath', '-literalpath']).forEach((y) => yolKontrol(y, cwd, kok, 'yaz'));
  } else if (ad === 'sed' || ad === 'perl') {
    const yerinde = args.some((a) => /^-[a-zA-Z]*i/.test(a) || a.startsWith('--in-place'));
    const dosyalar = konumsallar.slice(ad === 'sed' && !args.some((a) => a === '-e' || a === '-f') ? 1 : 0);
    dosyalar.forEach((y) => yolKontrol(y, cwd, kok, yerinde ? 'yaz' : 'oku'));
  }
}

function mevcutDal(dizin) {
  try {
    return execFileSync('git', ['--no-optional-locks', 'rev-parse', '--abbrev-ref', 'HEAD'], {
      cwd: dizin, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], timeout: 5000, windowsHide: true,
    }).trim();
  } catch {
    return null;
  }
}

const GIT_DEGERLI_SECENEK = new Set(['-C', '-c', '--git-dir', '--work-tree', '--namespace', '--exec-path', '--super-prefix', '--config-env']);

function gitKontrol(args, cwd, kok, korumali) {
  let dizin = cwd || kok;
  let i = 0;
  for (; i < args.length && secenekMi(args[i]); i++) {
    const a = args[i];
    const [ad, deger] = a.includes('=') ? [a.slice(0, a.indexOf('=')), a.slice(a.indexOf('=') + 1)] : [a, undefined];
    if (!GIT_DEGERLI_SECENEK.has(ad)) continue;
    const v = deger !== undefined ? deger : args[++i];
    if (ad === '-C' && v !== undefined) dizin = resolve(dizin, v);
    if (ad === '-c' && /^core\.hookspath=/i.test(v || '')) engelle('GIT-HOOKSPATH', 'git kancalarını devre dışı bırakmak (core.hooksPath) engellendi.');
  }
  const alt = args[i];
  const r = args.slice(i + 1);
  if (!alt) return;
  const dalKorumali = (d) => korumali.includes(dalAdi(d));
  let dal; // tembel
  const suankiDal = () => (dal === undefined ? (dal = mevcutDal(dizin)) : dal);
  const konum = r.filter((a) => !secenekMi(a));

  switch (alt) {
    case 'push': {
      if (r.some((a) => a === '--force' || a === '-f' || a.startsWith('--force-with-lease') || a === '--force-if-includes' || kisaKume(a, 'f'))) {
        engelle('GIT-PUSH-FORCE', 'zorla push (--force/-f/--force-with-lease) engellendi. Geçmişi yeniden yazmak yerine yeni commit atın.');
      }
      if (r.includes('--mirror') || r.includes('--all')) engelle('GIT-PUSH-TUMU', '--mirror/--all ile push korumalı dalları da iter; dalı açıkça belirtin.');
      if (r.includes('--no-verify')) engelle('GIT-NO-VERIFY', 'push --no-verify ile git kancalarını atlamak engellendi.');
      const sil = r.includes('--delete') || r.includes('-d');
      const refspecler = konum.slice(1); // ilk konumsal = uzak depo
      for (const spec of refspecler) {
        if (spec.startsWith('+')) engelle('GIT-PUSH-FORCE', `"+${spec.slice(1)}" refspec'i zorla push'tur; engellendi.`);
        const hedef = spec.includes(':') ? spec.slice(spec.indexOf(':') + 1) : spec;
        const hedefDal = hedef === 'HEAD' ? suankiDal() : hedef;
        if (hedefDal && dalKorumali(hedefDal)) {
          engelle(sil || spec.startsWith(':') ? 'GIT-DAL-SIL' : 'GIT-KORUMALI-PUSH',
            `korumalı dala (${dalAdi(hedefDal)}) doğrudan push/silme yapılmaz; çalışma dalına push edip PR açın.`);
        }
      }
      if (!refspecler.length && suankiDal() && dalKorumali(suankiDal())) {
        engelle('GIT-KORUMALI-PUSH', `şu an korumalı daldasınız (${suankiDal()}); refspec'siz push bu dalı iter. Bir çalışma dalına geçin.`);
      }
      return;
    }
    case 'commit':
      if (r.includes('--no-verify') || r.some((a) => kisaKume(a, 'n'))) engelle('GIT-NO-VERIFY', 'commit --no-verify/-n ile git kancalarını atlamak engellendi.');
    // eslint-disable-next-line no-fallthrough
    case 'merge': case 'rebase': case 'cherry-pick': case 'revert': case 'am': {
      if (r.some((a) => ['--abort', '--quit', '--skip'].includes(a))) return;
      const d = suankiDal();
      if (d && dalKorumali(d)) {
        engelle('GIT-KORUMALI-DAL', `korumalı dalda (${d}) ${alt} yapılmaz. Önce bir çalışma dalına geçin (ör. git switch -c <dal>); ana dala yalnız PR ile girilir.`);
      }
      return;
    }
    case 'reset':
      if (r.includes('--hard')) engelle('GIT-RESET-HARD', 'reset --hard kaydedilmemiş işi geri dönüşsüz siler; engellendi. Gerekirse git stash kullanın.');
      return;
    case 'clean':
      if ((r.includes('--force') || r.some((a) => kisaKume(a, 'f'))) && !r.includes('-n') && !r.includes('--dry-run') && !r.some((a) => kisaKume(a, 'n'))) {
        engelle('GIT-CLEAN', 'clean -f izlenmeyen dosyaları geri dönüşsüz siler; önce "git clean -n" ile bakın, silme işini insan yapsın.');
      }
      return;
    case 'filter-branch': case 'filter-repo':
      engelle('GIT-GECMIS-YAZ', `${alt} geçmişi yeniden yazar; engellendi.`);
      return;
    case 'update-ref':
      if (r.some((a) => /^refs\/heads\//.test(a) && dalKorumali(a))) engelle('GIT-KORUMALI-DAL', 'korumalı dal referansını doğrudan değiştirmek engellendi.');
      return;
    case 'branch': {
      const var_ = (...s) => r.some((a) => s.includes(a));
      const sil = var_('-d', '-D', '--delete') || r.some((a) => kisaKume(a, 'd') || kisaKume(a, 'D'));
      const tasi = var_('-m', '-M', '--move');
      const zorla = var_('-f', '--force');
      // silme: adı geçen herhangi bir korumalı dal; taşıma: kaynak (2 ad varsa ilki, yoksa mevcut dal);
      // zorla: yalnız yeniden işaret edilen dal (ilk ad) — "git branch -f ozellik main" zararsızdır.
      const kaynak = konum.length >= 2 ? konum[0] : suankiDal();
      if ((sil && konum.some(dalKorumali)) || (tasi && kaynak && dalKorumali(kaynak)) || (zorla && !tasi && konum[0] && dalKorumali(konum[0]))) {
        engelle('GIT-KORUMALI-DAL', 'korumalı dalı silmek/yeniden adlandırmak/zorla taşımak engellendi.');
      }
      return;
    }
    case 'checkout': case 'switch': {
      const i2 = r.findIndex((a) => a === '-B' || a === '-C' || a === '--force-create');
      if (i2 !== -1 && r[i2 + 1] && dalKorumali(r[i2 + 1])) engelle('GIT-KORUMALI-DAL', `korumalı dalı (${r[i2 + 1]}) zorla yeniden oluşturmak engellendi.`);
      return;
    }
    case 'config':
      if (konum.some((a) => /^core\.hookspath$/i.test(a)) && konum.length >= 2) engelle('GIT-HOOKSPATH', 'git kancalarını devre dışı bırakmak (core.hooksPath) engellendi.');
      return;
    default:
  }
}

const ALT_KABUK = { bash: ['-c'], sh: ['-c'], zsh: ['-c'], dash: ['-c'], ksh: ['-c'], powershell: ['-command', '-c'], pwsh: ['-command', '-c'], cmd: ['/c', '/k'] };

/** Bir komut metnini kurallara karşı denetler; ihlalde süreçten çıkar (exit 2). */
export function denetle(metin, { cwd, kok = projeKoku(), korumali = ayarOku(kok).korumali_dallar } = {}, derinlik = 0) {
  let dizin = cwd || kok;
  for (const { kelimeler, yonler } of parcala(String(metin || ''))) {
    for (const { op, hedef } of yonler) {
      if (op.startsWith('<<')) continue; // here-string/heredoc: dosya değil
      yolKontrol(hedef, dizin, kok, op.includes('<') ? 'oku' : 'yaz');
    }
    const [ad, ...args] = asilKomut(kelimeler);
    if (!ad) continue;
    // "cd x && git …": sonraki parçalar yeni dizinde değerlendirilir
    if (['cd', 'set-location', 'sl', 'chdir', 'pushd', 'push-location'].includes(ad)) {
      const hedefDizin = args.filter((a) => !secenekMi(a))[0];
      if (hedefDizin) dizin = resolve(dizin, hedefDizin.replace(/^~(?=[\\/]|$)/, process.env.HOME || process.env.USERPROFILE || '~'));
      continue;
    }
    const altKabuk = ALT_KABUK[ad];
    if (altKabuk && derinlik < 3) {
      const i = args.findIndex((a) => altKabuk.includes(a.toLowerCase()));
      if (i !== -1) denetle(args.slice(i + 1).join(' '), { cwd: dizin, kok, korumali }, derinlik + 1);
    }
    if (ad === 'git') gitKontrol(args, dizin, kok, korumali);
    else dosyaKomutKontrol(ad, args, dizin, kok);
  }
}

// ---------------- giriş noktası ----------------

if (process.argv[1] && basename(process.argv[1]) === 'kanca-komut.mjs') {
  guvenliCalistir(() => {
    const girdi = girdiOku();
    const komut = girdi.tool_input && girdi.tool_input.command;
    if (typeof komut !== 'string' || !komut.trim()) return;
    denetle(komut, { cwd: girdi.cwd });
  });
}
