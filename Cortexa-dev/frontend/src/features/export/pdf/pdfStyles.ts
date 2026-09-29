import { StyleSheet } from '@react-pdf/renderer';

export const PDF_COLORS = {
  textPrimary: '#1A1A1A',
  textSecondary: '#5C5C5C',
  brandAccent: '#0F6CBD',
  divider: '#E0E0E0',
  positive: '#0E700E',
  neutral: '#8A6D00',
  muted: '#A4262C',
};

export const pdfBaseStyles = StyleSheet.create({
  page: {
    fontFamily: 'Helvetica',
    fontSize: 10,
    color: PDF_COLORS.textPrimary,
    paddingTop: 40,
    paddingBottom: 60,
    paddingHorizontal: 40,
    lineHeight: 1.4,
  },
  coverWordmark: {
    fontFamily: 'Helvetica-Bold',
    fontSize: 14,
    color: PDF_COLORS.brandAccent,
    marginBottom: 4,
  },
  h2: {
    fontFamily: 'Helvetica-Bold',
    fontSize: 14,
    color: PDF_COLORS.brandAccent,
  },
  h3: {
    fontFamily: 'Helvetica-Bold',
    fontSize: 11,
    color: PDF_COLORS.textPrimary,
  },
  body: {
    fontFamily: 'Helvetica',
    fontSize: 10,
    color: PDF_COLORS.textPrimary,
    lineHeight: 1.4,
  },
  caption: {
    fontFamily: 'Helvetica',
    fontSize: 8,
    color: PDF_COLORS.textSecondary,
  },
  hairline: {
    borderBottomWidth: 0.5,
    borderBottomColor: PDF_COLORS.divider,
    borderBottomStyle: 'solid',
    marginVertical: 6,
  },
  coverHairline: {
    borderBottomWidth: 0.5,
    borderBottomColor: PDF_COLORS.divider,
    borderBottomStyle: 'solid',
    marginBottom: 18,
    marginTop: 6,
  },
  rowContainer: {
    marginBottom: 10,
  },
  footer: {
    position: 'absolute',
    bottom: 30,
    left: 40,
    right: 40,
  },
  footerHairline: {
    borderBottomWidth: 0.5,
    borderBottomColor: PDF_COLORS.divider,
    borderBottomStyle: 'solid',
    marginBottom: 4,
  },
  footerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  footerText: {
    fontFamily: 'Helvetica',
    fontSize: 8,
    color: PDF_COLORS.textSecondary,
  },
});
