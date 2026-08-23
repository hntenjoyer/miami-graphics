import { LegalDocModal, Section } from './LegalDocModal';

interface Props {
  open: boolean;
  onClose: () => void;
}

const UPDATED_RU = '20 июня 2026';
const UPDATED_EN = 'June 20, 2026';

export function PrivacyModal({ open, onClose }: Props) {
  return (
    <LegalDocModal
      open={open}
      onClose={onClose}
      ru={{ title: 'Политика конфиденциальности', updated: UPDATED_RU, body: <RuBody /> }}
      en={{ title: 'Privacy Policy', updated: UPDATED_EN, body: <EnBody /> }}
    />
  );
}

function RuBody() {
  return (
    <>
      <p className="mb-7 text-[14px] leading-7 text-slate-600">
        Настоящая Политика описывает, какие персональные данные обрабатывает приложение
        Miami Graphics (далее - «Сервис»), с какими целями и на каких основаниях, кому они
        передаются и какими правами вы обладаете. Используя Сервис, вы подтверждаете, что
        ознакомились с настоящей Политикой.
      </p>

      <Section n="1" title="Оператор">
        Оператором (контролёром) персональных данных является Miami Graphics (США).
        Контакт для обращений по вопросам обработки данных: miamimodscontact@gmail.com.
      </Section>

      <Section n="2" title="Какие данные мы обрабатываем">
        В зависимости от использования Сервиса мы можем обрабатывать: адрес электронной
        почты и имя пользователя; защищённый (хешированный) пароль; изображение профиля
        (аватар); историю установок модификаций; пользовательские сборки и связанные с
        ними сведения (в т.ч. указанные вами параметры оборудования - модель мыши и
        монитора, чувствительность, DPI, разрешение, частота обновления, FPS - которые вы
        добровольно публикуете в сборке); токены сессий и технические данные,
        необходимые для работы приложения. Гостевой режим работает без регистрации и
        привязки к email.
      </Section>

      <Section n="3" title="Цели и правовые основания">
        Данные обрабатываются для: создания и обслуживания аккаунта и авторизации;
        предоставления функций Сервиса (библиотека, сборки, синхронизация настроек);
        связи с вами (например, коды подтверждения и сброса пароля); обеспечения
        безопасности и предотвращения злоупотреблений. Правовые основания - исполнение
        соглашения с вами, ваше согласие, а также наши законные интересы в обеспечении
        работоспособности и безопасности Сервиса.
      </Section>

      <Section n="4" title="Кому передаются данные (процессоры)">
        Для работы Сервиса мы привлекаем поставщиков, выступающих обработчиками данных по
        нашему поручению: Supabase (база данных и серверные функции; размещение - регион
        Индия); Resend (отправка электронной почты, в т.ч. кодов подтверждения; США);
        Cloudflare R2 (хранилище и доставка файлов); поставщики серверов (VPS) в
        СНГ и Швейцарии, используемые для маршрутизации трафика. Мы не
        продаём ваши данные третьим лицам.
      </Section>

      <Section n="5" title="Трансграничная передача">
        Поскольку часть инфраструктуры расположена за пределами вашей страны (в частности,
        Индия, США, Швейцария), обработка данных может включать их трансграничную передачу.
        Мы принимаем разумные меры для обеспечения надлежащего уровня защиты при такой
        передаче. (Раздел требует проверки юристом - в т.ч. в части требований 152-ФЗ о
        локализации данных граждан РФ и требований GDPR к передаче за пределы ЕС.)
      </Section>

      <Section n="6" title="Сроки хранения">
        Мы храним персональные данные не дольше, чем это необходимо для целей их обработки
        или чем требует применимое право. После удаления аккаунта связанные с ним данные
        удаляются или обезличиваются в разумный срок, за исключением сведений, хранение
        которых обязательно по закону. (Конкретные сроки - уточнить и зафиксировать.)
      </Section>

      <Section n="7" title="Ваши права">
        Вы вправе запросить доступ к своим данным, их исправление или удаление, ограничить
        или возразить против обработки, отозвать ранее данное согласие, а также (в
        применимых случаях) получить данные в переносимом формате. Для реализации прав
        обратитесь по адресу miamimodscontact@gmail.com. Вы также можете удалить аккаунт и
        связанные данные средствами приложения.
      </Section>

      <Section n="8" title="Защита данных">
        Мы применяем разумные технические и организационные меры для защиты данных,
        включая хеширование паролей и ограничение доступа. Тем не менее ни один способ
        передачи или хранения информации в интернете не обеспечивает абсолютной
        безопасности.
      </Section>

      <Section n="9" title="Данные несовершеннолетних">
        Сервис не предназначен для лиц младше 16 лет. Если вы не достигли необходимого по
        вашему законодательству возраста цифрового согласия, используйте Сервис только с
        согласия законного представителя. Если нам станет известно, что данные были
        предоставлены без необходимого согласия, мы примем меры к их удалению.
      </Section>

      <Section n="10" title="Изменения и контакты" last>
        Мы можем обновлять настоящую Политику; о существенных изменениях уведомим разумным
        способом с указанием даты вступления в силу. По любым вопросам, связанным с
        обработкой данных, пишите на miamimodscontact@gmail.com.
      </Section>
    </>
  );
}

function EnBody() {
  return (
    <>
      <p className="mb-7 text-[14px] leading-7 text-slate-600">
        This Policy describes what personal data the Miami Graphics application (the
        “Service”) processes, for what purposes and on what grounds, to whom it is
        disclosed, and what rights you have. By using the Service, you confirm that you
        have read this Policy.
      </p>

      <Section n="1" title="Operator">
        The operator (controller) of personal data is Miami Graphics (USA). Contact for
        data-processing inquiries: miamimodscontact@gmail.com.
      </Section>

      <Section n="2" title="What data we process">
        Depending on how you use the Service, we may process: your email address and
        username; a protected (hashed) password; profile image (avatar); modification
        install history; user builds and related information (including hardware parameters
        you provide - mouse and monitor model, sensitivity, DPI, resolution, refresh rate,
        FPS - which you voluntarily publish in a build); session tokens and technical data
        necessary for the application to function. Guest mode works without registration or
        an email address.
      </Section>

      <Section n="3" title="Purposes and legal bases">
        Data is processed to: create and maintain your account and authorization; provide
        Service features (library, builds, settings sync); communicate with you (e.g.,
        confirmation and password-reset codes); ensure security and prevent abuse. The
        legal bases are performance of the agreement with you, your consent, and our
        legitimate interests in maintaining the operability and security of the Service.
      </Section>

      <Section n="4" title="Who we share data with (processors)">
        To operate the Service, we engage providers acting as data processors on our
        behalf: Supabase (database and server functions; hosted in the India region);
        Resend (sending email, including confirmation codes; USA); Cloudflare R2 (file
        storage and delivery); server (VPS) providers in the CIS and
        Switzerland used for traffic routing. We do not sell your data to third parties.
      </Section>

      <Section n="5" title="Cross-border transfer">
        Because part of the infrastructure is located outside your country (in particular
        India, the USA, Switzerland), data processing may involve cross-border transfers.
        We take reasonable measures to ensure an adequate level of protection for such
        transfers. (This section requires review by a lawyer - including with respect to
        the data-localization requirements of Russian law (152-FZ) for Russian citizens
        and GDPR requirements for transfers outside the EU.)
      </Section>

      <Section n="6" title="Retention periods">
        We retain personal data no longer than necessary for the purposes of processing or
        as required by applicable law. After account deletion, the associated data is
        deleted or anonymized within a reasonable time, except for information whose
        retention is required by law. (Specific periods to be defined and recorded.)
      </Section>

      <Section n="7" title="Your rights">
        You have the right to request access to your data, its correction or deletion, to
        restrict or object to processing, to withdraw previously given consent, and (where
        applicable) to receive your data in a portable format. To exercise your rights,
        contact miamimodscontact@gmail.com. You can also delete your account and associated
        data within the application.
      </Section>

      <Section n="8" title="Data protection">
        We apply reasonable technical and organizational measures to protect data,
        including password hashing and access restrictions. Nevertheless, no method of
        transmitting or storing information over the internet provides absolute security.
      </Section>

      <Section n="9" title="Children's data">
        The Service is not intended for persons under 16. If you have not reached the age
        of digital consent required by your law, use the Service only with the consent of a
        legal guardian. If we become aware that data was provided without the required
        consent, we will take steps to delete it.
      </Section>

      <Section n="10" title="Changes and contacts" last>
        We may update this Policy; we will notify you of material changes by reasonable
        means, indicating the effective date. For any questions related to data
        processing, write to miamimodscontact@gmail.com.
      </Section>
    </>
  );
}
