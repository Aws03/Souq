// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

// التسمية مربوطة بالحقل بلا تدخّل المستدعي — وحقل كلمة المرور منها. وجدته رحلة متصفّح على صفحة قبول الدعوة:
// getByLabel("New password") لم يجد شيئاً، أي أن قارئ الشاشة لم يجد اسماً أيضاً.
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (k) => k }) }));
const FormField = (await import('./FormField')).default;
const PasswordInput = (await import('./PasswordInput')).default;

describe('FormField', () => {
  it('يربط التسمية بحقل عادي وبحقل كلمة المرور، والرسالة بـ aria-describedby', () => {
    render(
      <>
        <FormField label="Email" hint="We never share it"><input type="email" /></FormField>
        <FormField label="New password" error="Password is required"><PasswordInput value="" onChange={() => {}} /></FormField>
      </>,
    );
    expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('We never share it');
    const password = screen.getByLabelText('New password');
    expect(password.tagName).toBe('INPUT');
    expect(password).toHaveAttribute('aria-invalid', 'true');
    expect(password).toHaveAccessibleDescription('Password is required');
  });

  it('معرّف المستدعي وhtmlFor يُحترمان', () => {
    render(
      <>
        <FormField label="Name"><input id="given-id" /></FormField>
        <FormField label="Explicit" htmlFor="elsewhere"><input id="elsewhere" /></FormField>
      </>,
    );
    expect(screen.getByLabelText('Name')).toHaveAttribute('id', 'given-id');
    expect(screen.getByLabelText('Explicit')).toHaveAttribute('id', 'elsewhere');
  });

  it('لا يربط التسمية بغلاف ليس حقلاً', () => {
    const { container } = render(
      <FormField label="Options"><label><input type="checkbox" /> Active</label></FormField>,
    );
    expect(container.querySelector('label[for]')).toBeNull();
  });
});
