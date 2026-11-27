import { useMemo, useState } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { Link } from 'react-router-dom';
import { query } from '../api';
import { useResource } from '../hooks';
import { date, useLocale } from '../i18n';
import { Button, ErrorBox, Loading } from '../ui';

type CalendarGame = {
    id: number;
    title: string;
    platform: string;
    releaseDateUtc: string;
    releaseYear: number | null;
    releaseDatePrecision: 'Exact' | 'Month' | 'Year' | null;
};
type UiSettings = {
    firstDayOfWeek: number;
    calendarHeaderFormat: 'ShortWeekday' | 'ShortWeekdayDate' | 'LongWeekday';
};

export default function CalendarPage() {
    const { t, locale } = useLocale();
    const settings = useResource<UiSettings>('settings');
    const [offset, setOffset] = useState(0);
    const days = useMemo(() => {
        const now = new Date();
        now.setHours(0, 0, 0, 0);
        const first = settings.data?.firstDayOfWeek ?? 1;
        const delta = (now.getDay() - first + 7) % 7;
        now.setDate(now.getDate() - delta + offset * 7);
        return Array.from({ length: 7 }, (_, index) => {
            const value = new Date(now);
            value.setDate(now.getDate() + index);
            return value;
        });
    }, [offset, settings.data?.firstDayOfWeek]);
    const start = days[0];
    const end = new Date(days[6]);
    end.setDate(end.getDate() + 1);
    const data = useResource<CalendarGame[]>(
        settings.data
            ? `games/calendar?${query({ startUtc: start.toISOString(), endUtc: end.toISOString() })}`
            : null,
    );
    const header = (day: Date) => {
        const format = settings.data?.calendarHeaderFormat ?? 'ShortWeekdayDate';
        if (format === 'LongWeekday') return new Intl.DateTimeFormat(locale, { weekday: 'long' }).format(day);
        if (format === 'ShortWeekday')
            return new Intl.DateTimeFormat(locale, { weekday: 'short' }).format(day);
        return `${new Intl.DateTimeFormat(locale, { weekday: 'short' }).format(day)} ${date(day, locale, 'short')}`;
    };
    return (
        <section className="admin-page compact-page calendar-page">
            <header className="view-toolbar">
                <h1>{t('Kalender', 'Calendar')}</h1>
                <div className="actions">
                    <Button
                        aria-label={t('Vorherige Woche', 'Previous week')}
                        onClick={() => setOffset(offset - 1)}
                    >
                        <ChevronLeft />
                    </Button>
                    <Button onClick={() => setOffset(0)}>{t('Heute', 'Today')}</Button>
                    <Button
                        aria-label={t('Nächste Woche', 'Next week')}
                        onClick={() => setOffset(offset + 1)}
                    >
                        <ChevronRight />
                    </Button>
                </div>
            </header>
            <ErrorBox
                error={settings.error || data.error}
                retry={() => {
                    settings.reload();
                    data.reload();
                }}
            />
            {!data.data && data.loading ? (
                <Loading />
            ) : (
                <div className="week-calendar">
                    {days.map((day) => {
                        const key = day.toISOString().slice(0, 10);
                        const games =
                            data.data?.filter((game) => game.releaseDateUtc?.slice(0, 10) === key) ?? [];
                        return (
                            <section key={key}>
                                <h2>{header(day)}</h2>
                                {games.map((game) => (
                                    <Link key={game.id} to={`/games/${game.id}`} className="calendar-event">
                                        <strong>
                                            {game.releaseDatePrecision === 'Exact' ? '' : '≈ '}
                                            {game.title}
                                        </strong>
                                        <small>{game.platform}</small>
                                    </Link>
                                ))}
                            </section>
                        );
                    })}
                </div>
            )}
            <p className="hint">
                {t(
                    '≈ kennzeichnet einen ungenauen Veröffentlichungstermin.',
                    '≈ marks an imprecise release date.',
                )}
            </p>
        </section>
    );
}
