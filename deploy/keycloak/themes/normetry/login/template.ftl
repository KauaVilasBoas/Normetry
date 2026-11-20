<#import "footer.ftl" as loginFooter>
<#macro icon name>
    <svg class="normetry-icon" aria-hidden="true"><use href="${url.resourcesPath}/img/icons.svg#${name}" /></svg>
</#macro>
<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false>
<!DOCTYPE html>
<html lang="${lang}"<#if realm.internationalizationEnabled> dir="${(locale.rtl)?then('rtl','ltr')}"</#if>>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="color-scheme" content="dark">
    <title>${title!msg("loginAccountTitle")} · Normetry</title>
    <link rel="icon" type="image/svg+xml" href="${url.resourcesPath}/img/favicon.svg">
    <link rel="preload" href="${url.resourcesPath}/fonts/inter-latin-wght-normal.woff2" as="font" type="font/woff2" crossorigin>
    <#if properties.stylesCommon?has_content>
        <#list properties.stylesCommon?split(' ') as style>
            <link href="${url.resourcesCommonPath}/${style}" rel="stylesheet">
        </#list>
    </#if>
    <#if properties.styles?has_content>
        <#list properties.styles?split(' ') as style>
            <link href="${url.resourcesPath}/${style}" rel="stylesheet">
        </#list>
    </#if>
    <#if properties.scripts?has_content>
        <#list properties.scripts?split(' ') as script>
            <script src="${url.resourcesPath}/${script}" defer></script>
        </#list>
    </#if>
    <#-- Keep the upstream scripts for locale menus, passkeys, cross-tab SSO and session expiry. -->
    <script type="importmap">
        { "imports": { "rfc4648": "${url.resourcesCommonPath}/vendor/rfc4648/rfc4648.js" } }
    </script>
    <script src="${url.resourcesPath}/js/menu-button-links.js" type="module"></script>
    <#if scripts??>
        <#list scripts as script>
            <script src="${script}" type="text/javascript"></script>
        </#list>
    </#if>
    <script type="module">
        <#outputformat "JavaScript">
        import { startSessionPolling } from ${(url.resourcesPath + "/js/authChecker.js")?c};
        startSessionPolling(${url.ssoLoginInOtherTabsUrl?c});
        </#outputformat>
    </script>
    <script type="module">
        document.addEventListener("click", (event) => {
            const link = event.target.closest("a[data-once-link]");
            if (!link) return;
            if (link.getAttribute("aria-disabled") === "true") {
                event.preventDefault();
                return;
            }
            const { disabledClass } = link.dataset;
            if (disabledClass) link.classList.add(...disabledClass.trim().split(/\s+/));
            link.setAttribute("role", "link");
            link.setAttribute("aria-disabled", "true");
        });
    </script>
    <#if authenticationSession??>
        <script type="module">
            <#outputformat "JavaScript">
            import { checkAuthSession } from ${(url.resourcesPath + "/js/authChecker.js")?c};
            checkAuthSession(${authenticationSession.authSessionIdHash?c});
            </#outputformat>
        </script>
    </#if>
</head>
<body class="normetry-auth ${bodyClass}" data-page-id="login-${pageId}" data-signing-in="${msg('normetrySigningIn')}">
    <main class="normetry-layout">
        <div class="normetry-brand">
            <#-- The SPA's Logo (src/web/src/ui/Logo.tsx) at its 40px auth-screen height. -->
            <div class="normetry-wordmark">
                <svg viewBox="0 0 382 100" width="153" height="40" role="img" aria-label="Normetry">
                    <defs>
                        <linearGradient id="normetry-mark" x1="12" y1="10" x2="88" y2="90" gradientUnits="userSpaceOnUse">
                            <stop offset="0" style="stop-color: var(--logo-mint-light)" />
                            <stop offset="0.5" style="stop-color: var(--mint)" />
                            <stop offset="1" style="stop-color: var(--teal)" />
                        </linearGradient>
                        <mask id="normetry-mark-mask" maskUnits="userSpaceOnUse" x="-2" y="-2" width="104" height="104">
                            <rect x="-2" y="-2" width="104" height="104" fill="white" />
                            <rect x="11" y="47.6" width="15" height="4.8" fill="black" />
                            <rect x="11" y="63.6" width="15" height="4.8" fill="black" />
                            <rect x="11" y="79.6" width="15" height="4.8" fill="black" />
                            <path d="M77 9H89V21Z" fill="black" />
                            <path d="M77 9V21H89" fill="none" stroke="black" stroke-width="4.4" />
                        </mask>
                    </defs>
                    <g mask="url(#normetry-mark-mask)" fill="url(#normetry-mark)">
                        <path d="M12 10H34V90H12ZM66 10H88V90H66Z" />
                        <path d="M12 10H37L88 90H63Z" />
                    </g>
                    <path d="M79.2 15.31V18.8H82.69Z" style="fill: var(--logo-fold)" />
                    <text x="102" y="72.4" font-size="61.6" font-weight="600" letter-spacing="-0.74" fill="currentColor">Normetry</text>
                </svg>
            </div>
            <p class="normetry-mobile-tagline">${msg("normetryTagline")}</p>
        </div>

        <div class="normetry-form-column">
            <section class="normetry-card" aria-labelledby="kc-page-title">
                <header class="normetry-form-header">
                    <#if pageId == "login"><div class="normetry-sign-in-icon"><@icon name="log-in" /></div></#if>
                    <#if realm.internationalizationEnabled && locale.supported?size gt 1>
                        <div id="kc-locale" class="normetry-locale">
                            <div class="menu-button-links">
                                <button id="kc-current-locale-link" aria-haspopup="true" aria-expanded="false" aria-controls="language-switch1"><span class="sr-only">${msg("normetryLanguage")} </span>${locale.current}</button>
                                <ul role="menu" tabindex="-1" aria-labelledby="kc-current-locale-link" aria-activedescendant="" id="language-switch1">
                                    <#list locale.supported as language>
                                        <li role="none"><a role="menuitem" id="language-${language?index + 1}" href="${language.url}">${language.label}</a></li>
                                    </#list>
                                </ul>
                            </div>
                        </div>
                    </#if>
                    <h1 id="kc-page-title"><#nested "header"></h1>
                    <#if pageId == "login"><p class="normetry-form-description">${msg("normetrySignInBody")}</p></#if>
                    <#if displayRequiredFields><p class="subtitle"><span aria-hidden="true">*</span> ${msg("requiredFields")}</p></#if>
                    <#if auth?has_content && auth.showUsername() && !auth.showResetCredentials()>
                        <#nested "show-username">
                        <div id="kc-username">
                            <span id="kc-attempted-username">${auth.attemptedUsername}</span>
                            <a id="reset-login" href="${url.loginRestartFlowUrl}">${msg("restartLoginTooltip")}</a>
                        </div>
                    </#if>
                </header>
                <div id="kc-content">
                    <div id="kc-content-wrapper">
                        <#if displayMessage && message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
                            <div class="normetry-message" role="<#if message.type == 'error'>alert<#else>status</#if>">
                                ${kcSanitize(message.summary)?no_esc}
                            </div>
                        </#if>
                        <#nested "form">
                        <#if auth?has_content && auth.showTryAnotherWayLink()>
                            <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post">
                                <input type="hidden" name="tryAnotherWay" value="on">
                                <a href="#" id="try-another-way" onclick="document.forms['kc-select-try-another-way-form'].requestSubmit();return false;">${msg("doTryAnotherWay")}</a>
                            </form>
                        </#if>
                        <#nested "socialProviders">
                        <#if displayInfo><div id="kc-info"><#nested "info"></div></#if>
                    </div>
                </div>
                <@loginFooter.content/>
            </section>
            <#if pageId == "login" && !realm.registrationAllowed>
                <div class="normetry-access">
                    <p>${msg("normetryAccessTitle")}</p>
                    <p>${msg("normetryAccessBody")}</p>
                </div>
            </#if>
        </div>

        <section class="normetry-introduction" aria-labelledby="normetry-headline">
            <h2 id="normetry-headline">${msg("normetryHeadline")}<br><span>${msg("normetryHeadlineEnd")}</span></h2>
            <p class="normetry-description">${msg("normetryIntroduction")}</p>
            <ul class="normetry-features">
                <li>
                    <span class="normetry-feature-icon"><@icon name="book-open-check" /></span>
                    <div><h3>${msg("normetryAnswersTitle")}</h3><p>${msg("normetryAnswersBody")}</p></div>
                </li>
                <li>
                    <span class="normetry-feature-icon"><@icon name="building-2" /></span>
                    <div><h3>${msg("normetryEstablishmentsTitle")}</h3><p>${msg("normetryEstablishmentsBody")}</p></div>
                </li>
            </ul>
            <div class="normetry-coverage">
                <p>${msg("normetryCoverage")}</p>
                <ul><li>FDA Food Code</li><li>EU 852/2004</li><li>Anvisa RDC 216/2004</li></ul>
            </div>
        </section>

        <footer class="normetry-footer"><span>Normetry</span><span>${msg("normetryFooter")}</span></footer>
    </main>
</body>
</html>
</#macro>
