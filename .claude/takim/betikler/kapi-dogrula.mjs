// takim-kur — kanıt doğrulayıcı: şu anki çalışma ağacı için YEŞİL bir test kapısı kanıtı var mı?
// Kopyalanır: .claude/takim/betikler/kapi-dogrula.mjs  (elle değiştirmeyin; /takim-kur günceller)
//
//   node .claude/takim/betikler/kapi-dogrula.mjs
// Çıkış 0 = GEÇERLİ (bu ağaç için yeşil kanıt var); 1 = BAYAT / YOK / YEŞİL DEĞİL.
// "Bitti" demeden önce gözden geçirici bunu (ve gerekirse kapının kendisini) koşturur.

import { readFileSync, existsSync } from 'node:fs';
import { join, basename } from 'node:path';
import { projeKoku, agacOzeti } from './ortak.mjs';

export function dogrula(kok = projeKoku()) {
  const ozet = agacOzeti(kok);
  if (!ozet) return { gecerli: false, neden: 'git deposu değil: kanıt bir çalışma ağacına bağlanamaz' };
  const dizin = join(kok, '.claude', 'takim', 'kanit');
  const yol = join(dizin, `${ozet.slice(0, 16)}.json`);
  if (existsSync(yol)) {
    const k = JSON.parse(readFileSync(yol, 'utf8'));
    if (k.agac_ozeti !== ozet) return { gecerli: false, neden: 'kanıt dosyası özetle uyuşmuyor (elle değiştirilmiş olabilir)' };
    if (k.durum !== 'yesil') return { gecerli: false, neden: `bu ağaç için son kapı sonucu "${k.durum}"`, kanit: k };
    return { gecerli: true, kanit: k };
  }
  const sonYol = join(dizin, 'son.json');
  if (existsSync(sonYol)) {
    const k = JSON.parse(readFileSync(sonYol, 'utf8'));
    return { gecerli: false, neden: `BAYAT: son kapı koşusundan (${k.zaman}, ${k.durum}) sonra kod değişti — kapı yeniden koşturulmalı`, kanit: k };
  }
  return { gecerli: false, neden: 'YOK: bu projede test kapısı hiç koşturulmamış' };
}

if (process.argv[1] && basename(process.argv[1]) === 'kapi-dogrula.mjs') {
  const r = dogrula();
  if (r.gecerli) {
    console.log(`GEÇERLİ: bu çalışma ağacı için yeşil kanıt var (${r.kanit.zaman}, koşan test ${r.kanit.toplam_test}).`);
    process.exit(0);
  }
  console.log(`GEÇERSİZ: ${r.neden}`);
  process.exit(1);
}
