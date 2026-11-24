# Публикация

Репозиторий содержит только приложение; настроек, ключей и профилей здесь нет.

## Первый push

Если репозиторий уже создан на GitHub:

~~~powershell
git remote add origin https://github.com/meferspb/restartvpn.git
git push -u origin main
git push origin v0.1.2
~~~

Если репозитория ещё нет и установлен GitHub CLI:

~~~powershell
gh repo create meferspb/restartvpn --public --source=. --remote=origin --push
~~~

## Релиз

Тег `v*` запускает `.github/workflows/release.yml`: CI собирает
`RestartVPN-<version>.exe` и публикует его как asset. В exe нет настроек —
они создаются при первом запуске на машине пользователя.

Локальная проверка артефакта:

~~~powershell
powershell -File scripts\build.ps1
Get-FileHash dist\RestartVPN-*.exe -Algorithm SHA256
~~~
