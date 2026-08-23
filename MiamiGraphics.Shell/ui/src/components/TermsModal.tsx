import { LegalDocModal, Section } from './LegalDocModal';

interface Props {
  open: boolean;
  onClose: () => void;
}

const UPDATED_RU = '20 июня 2026';
const UPDATED_EN = 'June 20, 2026';

export function TermsModal({ open, onClose }: Props) {
  return (
    <LegalDocModal
      open={open}
      onClose={onClose}
      ru={{ title: 'Условия использования', updated: UPDATED_RU, body: <RuBody /> }}
      en={{ title: 'Terms of Use', updated: UPDATED_EN, body: <EnBody /> }}
    />
  );
}

function RuBody() {
  return (
    <>
      <p className="mb-7 text-[14px] leading-7 text-slate-600">
        Используя приложение Miami Graphics (далее - «Лаунчер» или «Сервис»),
        вы подтверждаете, что прочитали и принимаете настоящие Условия. Обработка
        персональных данных регулируется нашей Политикой конфиденциальности. Если вы
        не согласны - не используйте Сервис.
      </p>

      <Section n="1" title="Описание сервиса">
        Лаунчер - это инструмент для установки и настройки графических и
        сопутствующих модификаций для игры Grand Theft Auto V, а также для
        управления связанными сборками, библиотекой и аккаунтом пользователя.
        Для удобной загрузки Сервис может размещать (хостить) копии модификаций на
        собственной инфраструктуре. Сервис не является продуктом Rockstar Games или
        Take-Two Interactive и никак с ними не аффилирован.
      </Section>

      <Section n="2" title="Кто может пользоваться">
        Используя Сервис, вы заявляете, что достигли возраста, с которого по
        законодательству вашей страны можно самостоятельно заключать подобные
        соглашения (но не младше 16 лет). Если вы не достигли необходимого возраста,
        использование допускается только с согласия законного представителя. Вы также
        подтверждаете, что владеете легальной копией GTA V.
      </Section>

      <Section n="3" title="Аккаунт и безопасность">
        Вы отвечаете за сохранность данных для доступа к своему аккаунту и за все
        действия, совершённые под ним. Мы принимаем разумные технические и
        организационные меры для защиты ваших данных, однако ни один способ передачи
        или хранения информации в интернете не обеспечивает абсолютной безопасности.
        Рекомендуем использовать уникальный пароль, отличный от паролей в других
        сервисах.
      </Section>

      <Section n="4" title="Правила использования">
        Запрещается: пытаться взломать, декомпилировать или нарушить работу Сервиса;
        загружать или распространять чужой контент без прав на него, вредоносное ПО,
        незаконные материалы или данные третьих лиц; использовать Лаунчер для получения
        нечестного преимущества в онлайн-режимах игр; перепродавать или передавать
        доступ к Сервису третьим лицам от своего имени. Мы вправе модерировать и удалять
        размещённый пользователями контент, нарушающий эти правила.
      </Section>

      <Section n="5" title="Моды и сторонний контент">
        Модификации устанавливаются и используются вами на собственный риск. Правила
        допустимости тех или иных модификаций различаются от сервера к серверу: то, что
        разрешено на одном игровом сервере, может быть запрещено на другом. Вы обязаны
        самостоятельно ознакомиться с правилами конкретного сервера, на котором играете,
        и соблюдать их. Между вами и администрацией стороннего сервера возможны
        разногласия по поводу использования модификаций; их разрешение - ваша
        ответственность. Мы не контролируем правила сторонних серверов и не несём
        ответственности за последствия использования модификаций на них. Права на GTA V
        и связанные товарные знаки принадлежат их правообладателям; Сервис не аффилирован
        с Rockstar Games / Take-Two Interactive.
      </Section>

      <Section n="6" title="Сторонний контент и удаление по жалобе (takedown)">
        Модификации, доступные через Лаунчер, созданы третьими лицами; права на них
        принадлежат их авторам и правообладателям. Для удобной загрузки мы можем
        размещать копии таких модификаций на собственной инфраструктуре. Miami Graphics
        не является автором этих модификаций, не претендует на права на них и не несёт
        ответственности за их содержание; мы прилагаем разумные усилия, чтобы не
        размещать материалы, прямо запрещённые их правообладателями к повторному
        размещению. Если вы являетесь правообладателем и считаете, что размещённый
        материал нарушает ваши права, направьте уведомление на miamimodscontact@gmail.com
        с указанием: (1) спорного материала и ссылки на него в Сервисе; (2) ваших
        контактных данных; (3) подтверждения наличия прав и добросовестности обращения.
        Мы рассмотрим обращение в разумный срок и при подтверждении удалим или ограничим
        доступ к материалу. К пользователям, повторно нарушающим права третьих лиц, может
        применяться прекращение доступа.
      </Section>

      <Section n="7" title="Отсутствие гарантий">
        Сервис предоставляется «как есть» и «по мере доступности», без каких-либо
        гарантий. Функции могут меняться, прерываться или удаляться без предварительного
        уведомления.
      </Section>

      <Section n="8" title="Ограничение ответственности">
        В максимально допустимой применимым правом степени мы не несём ответственности
        за любой ущерб (включая повреждение файлов игры, потерю данных или блокировку
        игрового аккаунта), возникший в результате использования Сервиса или
        установленных через него модификаций. Настоящее ограничение не применяется в той
        мере, в какой ответственность не может быть исключена по закону.
      </Section>

      <Section n="9" title="Блокировка доступа">
        Мы вправе ограничить или прекратить ваш доступ к Сервису при нарушении настоящих
        Условий, прав третьих лиц или применимого права. Вы можете в любой момент удалить
        свой аккаунт и связанные с ним данные.
      </Section>

      <Section n="10" title="Изменения условий">
        Условия могут обновляться; о существенных изменениях мы уведомим разумным
        способом с указанием даты вступления в силу. Продолжая пользоваться Сервисом
        после изменений, вы принимаете обновлённую редакцию.
      </Section>

      <Section n="11" title="Оператор и контакты" last>
        Оператор Сервиса: Miami Graphics (США). По вопросам, связанным с настоящими
        Условиями и обработкой данных, а также по обращениям правообладателей пишите на
        miamimodscontact@gmail.com.
      </Section>
    </>
  );
}

function EnBody() {
  return (
    <>
      <p className="mb-7 text-[14px] leading-7 text-slate-600">
        By using the Miami Graphics application (the “Launcher” or “Service”), you confirm
        that you have read and accept these Terms. The processing of personal data is
        governed by our Privacy Policy. If you do not agree, do not use the Service.
      </p>

      <Section n="1" title="Service description">
        The Launcher is a tool for installing and configuring graphics and related
        modifications for Grand Theft Auto V, as well as for managing associated builds,
        your library and user account. For convenient downloading, the Service may host
        copies of modifications on its own infrastructure. The Service is not a product of
        Rockstar Games or Take-Two Interactive and is in no way affiliated with them.
      </Section>

      <Section n="2" title="Who may use the Service">
        By using the Service, you represent that you have reached the age at which you may
        independently enter into such agreements under the laws of your country (but not
        younger than 16). If you have not reached the required age, use is permitted only
        with the consent of a legal guardian. You also confirm that you own a legal copy
        of GTA V.
      </Section>

      <Section n="3" title="Account and security">
        You are responsible for keeping your account credentials secure and for all
        actions taken under your account. We take reasonable technical and organizational
        measures to protect your data; however, no method of transmitting or storing
        information over the internet provides absolute security. We recommend using a
        unique password that differs from passwords used on other services.
      </Section>

      <Section n="4" title="Acceptable use">
        You may not: attempt to hack, decompile or disrupt the Service; upload or
        distribute third-party content without the rights to it, malware, illegal
        materials or third parties' data; use the Launcher to gain an unfair advantage in
        online game modes; resell or transfer access to the Service to third parties on
        your behalf. We may moderate and remove user-submitted content that violates these
        rules.
      </Section>

      <Section n="5" title="Mods and third-party content">
        Modifications are installed and used at your own risk. The rules on which
        modifications are permitted vary from server to server: what is allowed on one
        game server may be prohibited on another. You must review and comply with the
        rules of the specific server on which you play. Disagreements may arise between you
        and the administration of a third-party server regarding the use of modifications;
        resolving them is your responsibility. We do not control the rules of third-party
        servers and are not responsible for the consequences of using modifications on
        them. The rights to GTA V and related trademarks belong to their respective owners;
        the Service is not affiliated with Rockstar Games / Take-Two Interactive.
      </Section>

      <Section n="6" title="Third-party content and takedown">
        The modifications available through the Launcher are created by third parties; the
        rights to them belong to their authors and rights holders. For convenient
        downloading, we may host copies of such modifications on our own infrastructure.
        Miami Graphics is not the author of these modifications, claims no rights to them
        and is not responsible for their content; we make reasonable efforts not to host
        materials that their rights holders have expressly prohibited from being
        re-hosted. If you are a rights holder and believe that hosted material infringes
        your rights, send a notice to miamimodscontact@gmail.com stating: (1) the material
        in question and a link to it within the Service; (2) your contact details; (3)
        confirmation that you hold the rights and that the request is made in good faith.
        We will review the request within a reasonable time and, if confirmed, remove or
        restrict access to the material. Access may be terminated for users who repeatedly
        infringe third parties' rights.
      </Section>

      <Section n="7" title="No warranty">
        The Service is provided “as is” and “as available”, without any warranties.
        Features may change, be interrupted or removed without prior notice.
      </Section>

      <Section n="8" title="Limitation of liability">
        To the maximum extent permitted by applicable law, we are not liable for any
        damage (including damage to game files, loss of data or suspension of a game
        account) arising from the use of the Service or modifications installed through it.
        This limitation does not apply to the extent that liability cannot be excluded by
        law.
      </Section>

      <Section n="9" title="Suspension of access">
        We may restrict or terminate your access to the Service in the event of a
        violation of these Terms, third parties' rights or applicable law. You may delete
        your account and associated data at any time.
      </Section>

      <Section n="10" title="Changes to the Terms">
        The Terms may be updated; we will notify you of material changes by reasonable
        means, indicating the effective date. By continuing to use the Service after
        changes, you accept the updated version.
      </Section>

      <Section n="11" title="Operator and contacts" last>
        Service operator: Miami Graphics (USA). For questions related to these Terms and
        data processing, as well as for rights holders' requests, write to
        miamimodscontact@gmail.com.
      </Section>
    </>
  );
}
