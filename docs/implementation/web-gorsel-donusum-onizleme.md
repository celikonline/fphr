# SenseİK web görsel dönüşüm önizlemesi

İlk teslim `/app` ana sayfasıdır. Önizleme, çalışan `senseik-web` konteynerini değiştirmeden aynı Docker ağındaki API'yi kullanır. Adres: <http://127.0.0.1:13009/app>. İK mesajları: <http://127.0.0.1:13009/app/messages>. Girişin sol marka paneli: <http://127.0.0.1:13009/login>.

Worktree kökünde:

```sh
docker build -f web/Dockerfile \
  --build-arg VITE_API_BASE_URL=/api \
  --build-arg VITE_SHOW_DEMO_CREDENTIALS=true \
  -t senseik-web-gorsel-preview .
docker run -d --name senseik-web-preview \
  --network senseik_default \
  -p 127.0.0.1:13009:8080 \
  senseik-web-gorsel-preview
```

`senseik_default` ağı ve `api` konteyneri yerel SenseİK yığınıyla önceden çalışıyor olmalıdır. Tarayıcıda mevcut demo hesabıyla giriş yapıldığında `/app` açılır. Konteyner yalnız yerel arayüzde dinler.

Önizleme işiniz bitince yalnız bu konteyneri durdurup kaldırın:

```sh
docker stop senseik-web-preview
docker rm senseik-web-preview
```

Yeniden derlemeden önce eski önizleme konteynerini durdurup kaldırın; ardından yukarıdaki `docker build` ve `docker run` komutlarını tekrarlayın.
